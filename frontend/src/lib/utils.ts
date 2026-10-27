import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

/** Combina classes do Tailwind resolvendo conflitos (padrão shadcn/ui). */
export const cn = (...inputs: ClassValue[]): string => twMerge(clsx(inputs))
