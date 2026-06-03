import type { TRQ_V1_Status, TRQ_V1_SubmitPayload } from './TRQ_V1_types'

/** Blank submit payload for a fresh travel request. */
export function emptyPayload(): TRQ_V1_SubmitPayload {
  return {
    travelType: 'Round Trip',
    departureCity: '',
    destinationCity: '',
    departDate: '',
    returnDate: '',
    chargeTo: '',
    travelPurpose: '',
    passportName: '',
    seatPreference: 'No preference',
    pickupRequired: 'No',
  }
}

export const TRAVEL_TYPE_OPTIONS = ['Round Trip', 'One Way']
export const SEAT_OPTIONS = ['Window', 'Aisle', 'No preference']
export const PICKUP_OPTIONS = ['Yes', 'No']

/** Localised display label for a backend status string. */
export function zhStatus(s: TRQ_V1_Status): string {
  switch (s) {
    case 'PendingManager':   return '待主管核准'
    case 'ResubmitRequired': return '退回補件'
    case 'Completed':        return '已核准'
    case 'Cancelled':        return '已撤回'
  }
}
