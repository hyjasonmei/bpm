/**
 * Stub kept after the legacy Flow Library admin screen was retired.
 * Onboarding (still mounted by V0 AI Kitchen) imports listBundles for
 * a "saved bundles" indicator pill and getBundleDraftHydration for the
 * `?bundle=<guid>` URL hydration path. Both are no-op now — V0 hands
 * the draft in via props instead of via the bundle library.
 */
import type { ImportDraftResult } from '@/types/flowLibrary'

export async function listBundles(): Promise<Array<unknown>> {
  return []
}

export async function getBundleDraftHydration(_id: string): Promise<ImportDraftResult> {
  throw new Error('Bundle library retired — hydrate drafts via V0 props instead.')
}
