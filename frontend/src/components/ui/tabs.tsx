import * as React from "react"
import * as TabsPrimitive from "@radix-ui/react-tabs"

import { cn } from "@/lib/utils"

/**
 * Abas da aplicação (feature 009, research R9): shadcn/ui sobre
 * `@radix-ui/react-tabs`, que já é a base do `Avatar`, do `Select` e do
 * `Separator` daqui (constituição §II).
 *
 * A aba ativa usa a borda inferior `border-brand` de 2 px, iguais ao cabeçalho
 * "Pedidos" que a `CustomerProfilePage` desenhava à mão: as abas têm de parecer
 * o mesmo elemento que substituem, senão a vendedora lê a tela como redesign.
 * O traço fica `-mb-px` para coincidir com a linha do `TabsList` em vez de
 * dobrá-la.
 *
 * `min-h-11` (44 px) é o alvo de toque mínimo da FR-028, não decoração.
 */
function Tabs({ className, ...props }: React.ComponentProps<typeof TabsPrimitive.Root>) {
  return (
    <TabsPrimitive.Root
      data-slot="tabs"
      className={cn("flex flex-col gap-3", className)}
      {...props}
    />
  )
}

function TabsList({ className, ...props }: React.ComponentProps<typeof TabsPrimitive.List>) {
  return (
    <TabsPrimitive.List
      data-slot="tabs-list"
      className={cn(
        "inline-flex items-center gap-2 border-b border-border/70 text-muted-foreground",
        className
      )}
      {...props}
    />
  )
}

function TabsTrigger({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Trigger>) {
  return (
    <TabsPrimitive.Trigger
      data-slot="tabs-trigger"
      className={cn(
        "-mb-px inline-flex min-h-11 items-center gap-2 border-b-2 border-transparent px-1 text-sm font-medium whitespace-nowrap transition-colors",
        "focus-visible:border-ring focus-visible:outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50",
        "disabled:pointer-events-none disabled:opacity-50",
        "data-[state=active]:border-brand data-[state=active]:text-foreground",
        "[&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
        className
      )}
      {...props}
    />
  )
}

function TabsContent({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Content>) {
  return (
    <TabsPrimitive.Content
      data-slot="tabs-content"
      className={cn("min-w-0 outline-none", className)}
      {...props}
    />
  )
}

export { Tabs, TabsList, TabsTrigger, TabsContent }
