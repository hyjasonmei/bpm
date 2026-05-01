import * as React from 'react'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/cn'

const buttonVariants = cva(
  'inline-flex items-center justify-center gap-1.5 rounded-md font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-40 disabled:pointer-events-none whitespace-nowrap',
  {
    variants: {
      variant: {
        default:     'bg-header text-white hover:bg-header-2 border border-transparent',
        primary:     'bg-primary text-white hover:bg-blue-700 border border-transparent',
        amber:       'bg-accent text-white hover:bg-amber-600 border border-transparent',
        outline:     'border border-rule bg-card text-ink hover:bg-slate-50',
        ghost:       'hover:bg-slate-100 text-ink-muted border border-transparent',
        destructive: 'border border-red-200 text-danger hover:bg-red-50',
        good:        'bg-good text-white hover:bg-green-700 border border-transparent',
      },
      size: {
        xs: 'h-6 px-2 text-xs',
        sm: 'h-8 px-3 text-sm',
        md: 'h-9 px-4 text-sm',
        lg: 'h-10 px-5 text-sm',
        icon: 'h-8 w-8 p-0',
      },
    },
    defaultVariants: { variant: 'default', size: 'sm' },
  },
)

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, ...props }, ref) => (
    <button ref={ref} className={cn(buttonVariants({ variant, size }), className)} {...props} />
  ),
)
Button.displayName = 'Button'
