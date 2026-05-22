import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

export const NTD_RATE = 30.19
export const fmtNTD = (v: number) => `NTD ${(v || 0).toLocaleString()}`
export const fmtUSD = (v: number) => `(USD ${((v || 0) / NTD_RATE).toFixed(2)})`
