# Tasks

## 1. AppLayout responsive

- [ ] 1.1 Refactor AppLayout to use Tailwind responsive classes
- [ ] 1.2 Create MobileSidebar.tsx (slide-in drawer)
- [ ] 1.3 Hamburger toggle in top bar (visible only on phone)
- [ ] 1.4 Tablet narrow icon-only sidebar variant

## 2. Form runtime responsive

- [ ] 2.1 RepeaterField: card-list rendering on phone (< 640 px)
- [ ] 2.2 TaskExecution: Stepper as top tracker on phone
- [ ] 2.3 Sticky bottom action bar on phone

## 3. Mock-up forms responsive (Tailwind classes only)

- [ ] 3.1 Add `md:`, `lg:` prefixes to LeaveForm.tsx layout
- [ ] 3.2 Same for GEEForm, GEVForm, APEForm, TRQForm, TEOForm, HWPForm, ITPRForm, EXTOBForm
- [ ] 3.3 Verify desktop visual byte-identical at 1280x720

## 4. Dashboard / table responsive

- [ ] 4.1 Search results table: horizontal-scroll wrapper on phone
- [ ] 4.2 Reports charts: ensure ResponsiveContainer wrapping
- [ ] 4.3 Admin tables: same treatment + sticky first column

## 5. Touch targets

- [ ] 5.1 Audit buttons; ensure min 44x44 px on phone
- [ ] 5.2 Disable drag-drop on touch in DepartmentsTree; add "Move to..." button

## 6. End-to-end verification

- [ ] 6.1 Boot bpm-ui; resize browser to 375x667 (iPhone-ish)
- [ ] 6.2 Verify hamburger + drawer
- [ ] 6.3 Open LeaveForm; verify single-column layout
- [ ] 6.4 Open TaskExecution; verify Stepper top + sticky bottom
- [ ] 6.5 Repeater field shows as cards
- [ ] 6.6 Test on real iPhone Safari + Chrome
- [ ] 6.7 At 1280 width, verify visuals byte-identical to pre-change for the 9 mock-up forms
- [ ] 6.8 **Demo guard at 1280 width**: byte-identical visuals; only mobile-only classes added

## 7. Commit

- [ ] 7.1 Commit in chunks (AppLayout; form runtime; mock-up form classes; dashboards; touch targets)
- [ ] 7.2 Push via GitKraken
