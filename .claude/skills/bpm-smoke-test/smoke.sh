#!/usr/bin/env bash
# flowcook BPM smoke test — happy + unhappy paths + the demo Reset feature.
# Hits the running bpm-svc (5290) + bpm-admin-svc (5266). Read-mostly except
# it submits throwaway cases; the final RESET section wipes back to seed-init.
#
# Version-agnostic: every flow is exercised at whatever version the bpm launcher
# currently serves (the highest Published version per flow code in the registry),
# NOT a hardcoded /v1. Deploy + publish a v2 and the suite follows it. The Reset
# section re-registers at the deployed runtime version so it never downgrades.
#
# Usage:  bash smoke.sh            # full suite
#         BPM=... ADM=... bash smoke.sh
#         SKIP_RESET=1 bash smoke.sh   # skip the destructive Reset section
#
# Exit code = number of failed checks (0 = all green).
set -uo pipefail
BPM="${BPM:-http://localhost:5290}"
ADM="${ADM:-http://localhost:5266}"

pass=0; fail=0
ok(){ echo "  PASS $1"; pass=$((pass+1)); }
no(){ echo "  FAIL $1"; fail=$((fail+1)); }
chk(){ if [ "$2" = "$3" ]; then ok "[$3] $1"; else no "exp $2 got $3 — $1"; fi; }
P(){ curl -s -o /dev/null -w '%{http_code}' "$@"; }       # -> status code
J(){ curl -s "$@"; }                                       # -> body
login(){ J -X POST "$BPM/api/dev/login" -H 'Content-Type: application/json' -d "{\"personaCode\":\"$1\"}" \
  | python3 -c "import sys,json;print(json.load(sys.stdin).get('token',''))" 2>/dev/null; }
field(){ python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('$1',''))" 2>/dev/null; }
roles(){ python3 -c "import sys,json,base64;p=sys.stdin.read().split('.')[1];p+='='*(-len(p)%4);r=json.loads(base64.urlsafe_b64decode(p)).get('roles',[]);print(','.join(sorted(r if isinstance(r,list) else [r])))" 2>/dev/null; }

# ---- live version resolution (what bpm shows) -----------------------------
# Resolve each flow at the version the bpm launcher actually serves: the highest
# Published version per flow code in the registry (latest-per-code + Published
# gate). macOS bash 3.2 has no associative arrays, so cache the registry JSON in
# REG and parse it on demand.
REG=""
load_flow_versions(){ REG=$(curl -s -H "Authorization: Bearer $1" "$BPM/api/flow-registry"); }
ver(){ echo "$REG" | python3 -c "import sys,json;c='$1';vs=[int(x['version']) for x in json.load(sys.stdin) if x.get('flowCode')==c and x.get('state')=='Published'];print(max(vs) if vs else 1)" 2>/dev/null || echo 1; }
# base route for a flow: rt LEAVE leave  ->  $BPM/api/leave/v<live>
rt(){ echo "$BPM/api/$2/v$(ver "$1")"; }

echo "######## flowcook smoke — $BPM / $ADM ########"

# ---- tokens (personas: employee=Bob, manager=Alice, finance=Frank, hr=Henry) ----
TE=$(login employee); TM=$(login manager); TF=$(login finance); TH=$(login hr); TA=$(login admin)

# ---- resolve live route per flow (follows whatever the launcher serves) ----
load_flow_versions "$TE"
R_LEAVE=$(rt LEAVE leave); R_TEO=$(rt TEO teo)
R_VENDOR=$(rt VENDOR_EXPENSE vendor-expense); R_PR=$(rt PURCHASE_REQUEST purchase-request)

echo "### A. health + identity"
chk "bpm-svc up" 200 "$(P "$BPM/api/flow-codes")"
chk "admin-svc up" 200 "$(P "$ADM/api/roles")"
chk "admin JWT = codes" "PERSONA_SWITCH,SYSTEM_ADMIN" "$(echo "$TA" | roles)"
chk "no empty role codes" 0 "$(curl -s "$ADM/api/roles" | python3 -c "import sys,json;print(sum(1 for r in json.load(sys.stdin) if not r.get('code')))")"
chk "10 flows published" 10 "$(curl -s -H "Authorization: Bearer $TE" "$BPM/api/flow-registry" | python3 -c "import sys,json;print(len(set(x['flowCode'] for x in json.load(sys.stdin) if x['state']=='Published')))")"
echo "  ---- live versions: LEAVE v$(ver LEAVE) · TEO v$(ver TEO) · VENDOR_EXPENSE v$(ver VENDOR_EXPENSE) · PURCHASE_REQUEST v$(ver PURCHASE_REQUEST) ----"

echo "### B. HAPPY — LEAVE short (manager -> HR_MANAGER dept-inherit -> Completed)"
CID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"leaveType":"Annual","dateRange":{"start":"2026-07-01","end":"2026-07-02"},"reason":"smoke"}' "$R_LEAVE" | field id)
mstat=$(J -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision" | field status)
chk "LEAVE manager-approve -> PendingHr" PendingHr "$mstat"
astat=$(J -X POST -H "Authorization: Bearer $TH" -H 'Content-Type: application/json' -d '{"archiveNote":"filed"}' "$R_LEAVE/$CID/hr-archive" | field status)
chk "LEAVE hr-archive -> Completed" Completed "$astat"

echo "### B. HAPPY — LEAVE long (>=7d -> VP step resolves)"
CID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"leaveType":"Annual","dateRange":{"start":"2026-09-01","end":"2026-09-10"},"reason":"long"}' "$R_LEAVE" | field id)
vstat=$(J -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision" | field status)
chk "LEAVE long manager-approve -> PendingVp" PendingVp "$vstat"

echo "### B. HAPPY — TEO (manager -> FINANCE -> Completed)"
CID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"travelRequestNo":"TR-1","expenseItems":[{"date":"2026-06-01","amount":"100"}]}' "$R_TEO" | field id)
tstat=$(J -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_TEO/$CID/manager-decision" | field status)
chk "TEO manager-approve -> PendingFinance" PendingFinance "$tstat"
fstat=$(J -X POST -H "Authorization: Bearer $TF" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_TEO/$CID/finance-decision" | field status)
chk "TEO finance-approve -> Completed" Completed "$fstat"

echo "### B. HAPPY — VENDOR_EXPENSE (supervisor -> PROCUREMENT)"
CID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"vendor":"V","submitterComment":"c","invoices":[{"invoiceDate":"2026-06-01","amount":"10"}]}' "$R_VENDOR" | field id)
vestat=$(J -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_VENDOR/$CID/supervisor-decision" | field status)
chk "VENDOR supervisor-approve -> PendingProcurement" PendingProcurement "$vestat"

echo "### B. HAPPY — PURCHASE_REQUEST (dept-head -> FINANCE)"
CID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"submitterComment":"c","invoices":[{"invoiceDate":"2026-06-01","amount":"10"}]}' "$R_PR" | field id)
prstat=$(J -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_PR/$CID/dept-head-decision" | field status)
chk "PURCHASE dept-head-approve -> PendingFinance" PendingFinance "$prstat"

echo "### B. HAPPY — manager-only flows submit-alive (APE/EOB/ETM/FAD/FAP/TRQ)"
for f in ape eob etm fad fap trq; do
  CODE=$(echo "$f" | tr 'a-z' 'A-Z')
  c=$(P -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{}' "$BPM/api/$f/v$(ver "$CODE")")
  case "$c" in 201|400|422) ok "[$c] $f submit endpoint alive (v$(ver "$CODE"))";; *) no "$f submit -> $c";; esac
done

echo "### C. UNHAPPY — validation (400)"
chk "LEAVE end<start" 400 "$(P -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"leaveType":"Annual","dateRange":{"start":"2026-07-10","end":"2026-07-01"},"reason":"x"}' "$R_LEAVE")"
chk "LEAVE empty reason" 400 "$(P -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"leaveType":"Annual","dateRange":{"start":"2026-07-01","end":"2026-07-02"},"reason":""}' "$R_LEAVE")"
chk "TEO empty items" 400 "$(P -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"travelRequestNo":"T","expenseItems":[]}' "$R_TEO")"

echo "### C. UNHAPPY — authN (401)"
chk "no token" 401 "$(P -X POST -H 'Content-Type: application/json' -d '{}' "$R_LEAVE")"
chk "bad token" 401 "$(P -X POST -H 'Authorization: Bearer not.a.jwt' -H 'Content-Type: application/json' -d '{}' "$R_LEAVE")"

echo "### C. UNHAPPY — not found / wrong-actor / state guards"
RND=$(python3 -c "import uuid;print(uuid.uuid4())")
chk "manager-decision on random id" 404 "$(P -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$RND/manager-decision")"
CID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"leaveType":"Annual","dateRange":{"start":"2026-07-01","end":"2026-07-02"},"reason":"u"}' "$R_LEAVE" | field id)
chk "submitter approves own case" 403 "$(P -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision")"
chk "unrelated user approves" 403 "$(P -X POST -H "Authorization: Bearer $TF" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision")"
chk "hr-archive while PendingManager" 409 "$(P -X POST -H "Authorization: Bearer $TH" -H 'Content-Type: application/json' -d '{"archiveNote":"x"}' "$R_LEAVE/$CID/hr-archive")"
J -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision" >/dev/null
chk "double manager-decision" 409 "$(P -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision")"

echo "### C. UNHAPPY — reject -> terminal guard"
CID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"leaveType":"Annual","dateRange":{"start":"2026-07-05","end":"2026-07-06"},"reason":"r"}' "$R_LEAVE" | field id)
chk "manager reject -> Rejected" Rejected "$(J -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":false}' "$R_LEAVE/$CID/manager-decision" | field status)"
chk "act on Rejected case" 409 "$(P -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision")"

echo "### C. UNHAPPY — cross-flow ordering + admin gating"
VID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"vendor":"V","submitterComment":"c","invoices":[{"invoiceDate":"2026-06-01","amount":"10"}]}' "$R_VENDOR" | field id)
chk "VENDOR procurement before supervisor" 409 "$(P -X POST -H "Authorization: Bearer $TF" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_VENDOR/$VID/procurement-decision")"
chk "admin process-admin (SYSTEM_ADMIN)" 200 "$(P -H "Authorization: Bearer $TA" "$BPM/api/admin/process-admin/definitions")"
chk "employee process-admin denied" 403 "$(P -H "Authorization: Bearer $TE" "$BPM/api/admin/process-admin/definitions")"
chk "employee persona-switch denied" 403 "$(P -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"targetEmail":"bob@acme.example"}' "$BPM/api/sandbox/persona")"

echo "### C. UNHAPPY — delegation authz cycle"
FRANK=$(curl -s "$ADM/api/principals" | python3 -c "import sys,json;[print(p['id']) for p in json.load(sys.stdin) if p.get('email')=='frank@acme.example']" 2>/dev/null | head -1)
chk "delegate to self -> 400" 400 "$(P -X PUT -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d "{\"delegateUserId\":\"$(curl -s "$ADM/api/principals" | python3 -c "import sys,json;[print(p['id']) for p in json.load(sys.stdin) if p.get('email')=='alice@acme.example']" 2>/dev/null | head -1)\",\"startAt\":\"2026-06-01T00:00:00Z\",\"endAt\":\"2026-12-31T00:00:00Z\"}" "$BPM/api/delegation/mine")"
CID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"leaveType":"Annual","dateRange":{"start":"2026-07-01","end":"2026-07-02"},"reason":"d"}' "$R_LEAVE" | field id)
chk "before delegation: Frank denied" 403 "$(P -X POST -H "Authorization: Bearer $TF" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision")"
J -X PUT -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d "{\"delegateUserId\":\"$FRANK\",\"startAt\":\"2026-06-01T00:00:00Z\",\"endAt\":\"2026-12-31T00:00:00Z\"}" "$BPM/api/delegation/mine" >/dev/null
chk "during delegation: Frank allowed" 200 "$(P -X POST -H "Authorization: Bearer $TF" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision")"
J -X DELETE -H "Authorization: Bearer $TM" "$BPM/api/delegation/mine" >/dev/null
CID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"leaveType":"Annual","dateRange":{"start":"2026-07-03","end":"2026-07-04"},"reason":"d2"}' "$R_LEAVE" | field id)
chk "after revoke: Frank denied again" 403 "$(P -X POST -H "Authorization: Bearer $TF" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision")"

if [ "${SKIP_RESET:-0}" != "1" ]; then
echo "### D. RESET feature (factory-wipe -> reseed -> register/publish -> verify init + post-reset flow works)"
chk "factory-reset 200" 200 "$(P -X POST "$BPM/api/sandbox-admin/factory-reset")"
chk "reseed 200" 200 "$(P -X POST "$ADM/api/admin/reset/reseed")"
# Register at the DEPLOYED runtime version (flow-codes reports the highest <CODE>_V<N>_Case
# per flow) so reset re-publishes whatever's actually shipped — not a hardcoded v1.
FLOWS=$(curl -s "$BPM/api/flow-codes" | python3 -c "import sys,json;d=json.load(sys.stdin);print(json.dumps({'flows':[{'flowCode':f['flowCode'],'displayName':f['displayName'],'version':f.get('version',1)} for f in d]}))")
chk "register-shipped 10" 10 "$(J -X POST "$ADM/api/flows/register-shipped" -H 'Content-Type: application/json' -d "$FLOWS" | python3 -c "import sys,json;print(len(json.load(sys.stdin)['registered']))")"
# re-login (sessions/identity were reseeded), re-resolve live versions, confirm a flow still runs end-to-end
TE=$(login employee); TM=$(login manager); TH=$(login hr)
load_flow_versions "$TE"; R_LEAVE=$(rt LEAVE leave)
CID=$(J -X POST -H "Authorization: Bearer $TE" -H 'Content-Type: application/json' -d '{"leaveType":"Annual","dateRange":{"start":"2026-07-01","end":"2026-07-02"},"reason":"post-reset"}' "$R_LEAVE" | field id)
J -X POST -H "Authorization: Bearer $TM" -H 'Content-Type: application/json' -d '{"approve":true}' "$R_LEAVE/$CID/manager-decision" >/dev/null
chk "post-reset LEAVE runs to Completed (v$(ver LEAVE))" Completed "$(J -X POST -H "Authorization: Bearer $TH" -H 'Content-Type: application/json' -d '{"archiveNote":"ok"}' "$R_LEAVE/$CID/hr-archive" | field status)"
# final clean slate
J -X POST "$BPM/api/sandbox-admin/factory-reset" >/dev/null
fi

echo ""
echo "######## RESULT: $pass passed, $fail failed ########"
exit $fail
