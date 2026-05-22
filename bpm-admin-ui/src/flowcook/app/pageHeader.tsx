import {
  createContext,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from 'react'

export interface PageHeader {
  /** small uppercase kicker above the title; defaults to "flowcook · admin" */
  kicker?: string
  /** back chevron rendered before the title */
  back?: { label: string; onClick: () => void }
  /** main heading */
  title: string
  /** small grey text after the title (e.g. flow code + version) */
  subtitle?: string
  /** inline pills (e.g. lifecycle state) rendered after the title row */
  badges?: ReactNode
  /** right-aligned status (e.g. save state) */
  status?: ReactNode
}

interface PageHeaderContextValue {
  header: PageHeader | null
  setHeader: (h: PageHeader | null) => void
}

const PageHeaderContext = createContext<PageHeaderContextValue | null>(null)

export function PageHeaderProvider({ children }: { children: ReactNode }) {
  const [header, setHeader] = useState<PageHeader | null>(null)
  return (
    <PageHeaderContext.Provider value={{ header, setHeader }}>
      {children}
    </PageHeaderContext.Provider>
  )
}

export function usePageHeader(): PageHeader | null {
  return useContext(PageHeaderContext)?.header ?? null
}

/**
 * Push a page-level header into the shell while the calling component is
 * mounted. Caller must memoize the `header` value (e.g. via useMemo) so the
 * effect's identity is stable; otherwise the shell will re-render every
 * frame.
 */
export function useSetPageHeader(header: PageHeader | null) {
  const ctx = useContext(PageHeaderContext)
  useEffect(() => {
    if (!ctx) return
    ctx.setHeader(header)
    return () => { ctx.setHeader(null) }
  }, [ctx, header])
}
