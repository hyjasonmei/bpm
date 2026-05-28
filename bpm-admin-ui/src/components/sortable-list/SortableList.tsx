import { useId, type CSSProperties, type ReactNode } from 'react'
import { GripVertical } from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
  type SortableContextProps,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'

/**
 * Drag-handle bag passed to render-prop children. Mount `setNodeRef` on
 * the row root and `setActivatorNodeRef` + `attributes` + `listeners` on
 * the drag handle element (e.g. the GripVertical icon). Spread `style`
 * on the row root for the live transform during a drag.
 */
export interface SortableHandleProps {
  setNodeRef: (node: HTMLElement | null) => void
  setActivatorNodeRef: (node: HTMLElement | null) => void
  attributes: ReturnType<typeof useSortable>['attributes']
  listeners: ReturnType<typeof useSortable>['listeners']
  style: { transform: string | undefined; transition: string | undefined }
  isDragging: boolean
}

interface SortableListProps<T> {
  items: T[]
  getId: (item: T, idx: number) => string
  onReorder: (next: T[]) => void
  children: (item: T, idx: number, handle: SortableHandleProps) => ReactNode
  /** Override the sorting strategy (default vertical list). */
  strategy?: SortableContextProps['strategy']
}

/**
 * Vertical sortable list primitive. Wraps `@dnd-kit/core` + `sortable`
 * with the minimum boilerplate so per-row layout editors stay readable:
 *
 *   <SortableList items={rows} getId={r => r.id} onReorder={setRows}>
 *     {(row, idx, handle) => (
 *       <div ref={handle.setNodeRef} style={handle.style}>
 *         <button ref={handle.setActivatorNodeRef} {...handle.attributes} {...handle.listeners}>
 *           <GripVertical />
 *         </button>
 *         ...row content...
 *       </div>
 *     )}
 *   </SortableList>
 *
 * Only the drag handle should receive `attributes` + `listeners` so
 * inputs / selects inside the row stay clickable.
 */
export function SortableList<T>({ items, getId, onReorder, children, strategy }: SortableListProps<T>) {
  const sensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: { distance: 4 },
    }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  )
  const ids = items.map((it, idx) => getId(it, idx))
  const contextId = useId()

  function handleDragEnd(e: DragEndEvent) {
    const { active, over } = e
    if (!over || active.id === over.id) return
    const from = ids.indexOf(String(active.id))
    const to = ids.indexOf(String(over.id))
    if (from < 0 || to < 0) return
    onReorder(arrayMove(items, from, to))
  }

  return (
    <DndContext id={contextId} sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
      <SortableContext items={ids} strategy={strategy ?? verticalListSortingStrategy}>
        {items.map((item, idx) => (
          <SortableRow key={ids[idx]} id={ids[idx]}>
            {(handle) => children(item, idx, handle)}
          </SortableRow>
        ))}
      </SortableContext>
    </DndContext>
  )
}

/** Standard drag-handle button. Spread `handle` onto it; rendered as a
 *  GripVertical icon inside a button so it's keyboard / a11y friendly
 *  and inputs / selects in the same row stay clickable. */
export function DragGrip({ handle, className, iconClassName }: {
  handle: SortableHandleProps | undefined
  className?: string
  iconClassName?: string
}) {
  if (!handle) {
    return <GripVertical className={cn('h-3 w-3 shrink-0 text-ink-faint', iconClassName)} />
  }
  return (
    <button
      type="button"
      ref={handle.setActivatorNodeRef}
      {...handle.attributes}
      {...handle.listeners}
      title="拖曳排序"
      className={cn(
        'flex shrink-0 cursor-grab touch-none items-center justify-center text-ink-faint transition-colors hover:text-primary active:cursor-grabbing',
        className,
      )}
    >
      <GripVertical className={cn('h-3 w-3', iconClassName)} />
    </button>
  )
}

/** Merge dragHandle's transform style + user style. */
export function mergeDragStyle(handle: SortableHandleProps | undefined, extra?: CSSProperties): CSSProperties | undefined {
  if (!handle) return extra
  return { ...handle.style, ...extra }
}

function SortableRow({ id, children }: { id: string; children: (h: SortableHandleProps) => ReactNode }) {
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({ id })
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  }
  return <>{children({ setNodeRef, setActivatorNodeRef, attributes, listeners, style, isDragging })}</>
}
