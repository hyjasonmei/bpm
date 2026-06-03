/** Mirrors TRQ_V1_CaseStatus on the backend. */
export type TRQ_V1_Status =
  | 'PendingManager'
  | 'ResubmitRequired'
  | 'Completed'
  | 'Cancelled'

/** Submit / resubmit payload — dates are ISO yyyy-MM-dd strings on the wire. */
export interface TRQ_V1_SubmitPayload {
  travelType: string
  departureCity: string
  destinationCity: string
  departDate: string
  returnDate: string | null
  chargeTo: string
  travelPurpose: string
  passportName: string | null
  seatPreference: string | null
  pickupRequired: string | null
}

export interface TRQ_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface TRQ_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  travelType: string
  departureCity: string
  destinationCity: string
  departDate: string
  returnDate: string | null
  chargeTo: string
  travelPurpose: string
  passportName: string | null
  seatPreference: string | null
  pickupRequired: string | null
  status: TRQ_V1_Status
  roundCount: number
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  managerDecision: TRQ_V1_DecisionDto | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
