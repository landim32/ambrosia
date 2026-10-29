/**
 * Recurso unico pt-BR (FR-024). Toda string visivel do app vem daqui,
 * organizada por tela. `interpolation.escapeValue` fica desligado no
 * `i18n/index.ts` porque nenhum texto vem de entrada do usuario com HTML.
 */
export const ptBR = {
  /** Identidade do app (marca "Ambrosio"). */
  app: {
    name: 'Ambrosio',
    logoAlt: 'Ambrosio',
  },
  /** Navegação principal: barra superior no desktop, tab bar no rodapé no mobile. */
  nav: {
    mainLabel: 'Navegação principal',
    customers: 'Clientes',
    orders: 'Pedidos',
    import: 'Importar',
    /** Feature 008: a vendedora chega na configuração pelo menu (contracts/ui.md). */
    settings: 'Configurações',
    account: 'Conta',
    accountTitle: 'Sua conta',
    close: 'Fechar',
  },
  common: {
    logout: 'Sair',
    customers: 'Clientes',
    orders: 'Pedidos',
    previous: 'Anterior',
    next: 'Próxima',
    pageOf: 'Página {{page}} de {{pages}}',
    records_one: '{{count}} registro',
    records_other: '{{count}} registros',
    retry: 'Tentar de novo',
    loading: 'Carregando',
    backToFirstPage: 'Voltar à primeira página',
    clearSearch: 'Limpar busca',
    show: 'Mostrar',
    hide: 'Ocultar',
    showPassword: 'Mostrar senha',
    hidePassword: 'Ocultar senha',
    notAvailable: '—',
    results: 'Resultados',
    items: 'Itens',
    paginationLabel: 'Paginação',
  },
  login: {
    title: 'Entrar no Ambrosio',
    subtitle: 'Consulte clientes e pedidos da sua conta.',
    google: 'Entrar com Google',
    email: 'E-mail',
    password: 'Senha',
    submit: 'Entrar',
    or: 'ou',
    noAccount: 'Não tem conta?',
    createAccount: 'Criar conta',
    expired: 'Sua sessão expirou',
    signInLabel: 'Entrar com a conta Google',
    signOutLabel: 'Sair da sessão atual',
  },
  /** Tela de cadastro (003): e-mail e senha reaproveitam `login.email`/`login.password`. */
  register: {
    title: 'Criar conta',
    subtitle: 'Use seu e-mail e uma senha para acessar o Ambrosio.',
    name: 'Nome',
    confirmPassword: 'Confirmar senha',
    submit: 'Criar conta',
    haveAccount: 'Já tenho conta',
  },
  /** Mensagens de validação no cliente (data-model §8) — a API tem as suas próprias. */
  validation: {
    nameRequired: 'Informe seu nome',
    nameTooLong: 'O nome deve ter no máximo 120 caracteres',
    emailRequired: 'Informe seu e-mail',
    emailInvalid: 'E-mail inválido',
    emailTooLong: 'O e-mail deve ter no máximo 254 caracteres',
    passwordRequired: 'Informe sua senha',
    passwordLength: 'A senha deve ter entre 8 e 128 caracteres',
    passwordMismatch: 'As senhas não conferem',
    /** Regras da tela de importação (feature 006); os textos coincidem com o validator da API. */
    importStartDateRequired: 'Informe a data inicial do período.',
    importEndDateRequired: 'Informe a data final do período.',
    importStartDateAfterEndDate: 'A data inicial deve ser anterior ou igual à data final.',
    importEndDateInFuture: 'A data final não pode estar no futuro.',
    /** FR-010: o limite é em MESES (`maxRangeMonths` dos defaults), com o número na frase. */
    importRangeTooLong: 'O período não pode passar de {{max}} meses.',
    /**
     * Validação da tela de configuração (feature 008, contracts/ui.md). As frases
     * coincidem com as do `PUT /imports/mais-vendas/credentials` (contracts/api.md)
     * para a vendedora ler a mesma mensagem antes e depois do envio; nenhuma delas
     * repete o valor digitado (FR-003).
     */
    credentialsLoginRequired: 'Informe o login do +Vendas.',
    credentialsLoginTooLong: 'O login deve ter no máximo 120 caracteres.',
    credentialsPasswordRequired: 'Informe a senha do +Vendas.',
    credentialsPasswordTooLong: 'A senha deve ter no máximo 200 caracteres.',
    credentialsPasswordForLoginChange: 'Informe a senha para trocar o login.',
  },
  /**
   * Tela de configuração do +Vendas (feature 008, `/configuracoes/mais-vendas`,
   * FR-001 a FR-007): só as credenciais do +Vendas por enquanto. Os textos são os
   * de `contracts/ui.md`.
   */
  settings: {
    title: 'Configurações',
    subtitle: 'O que o Ambrosio usa por você: as credenciais do +Vendas ficam aqui e nunca aparecem de novo.',
    nav: 'Configurações',
    maisVendasSection: '+Vendas',
    login: 'Login do +Vendas',
    password: 'Senha do +Vendas',
    /** Estado sem credenciais (contracts/ui.md). */
    emptyHelp: 'Cadastre o login e a senha que você usa no +Vendas. A senha fica guardada protegida e nunca é exibida.',
    /** Com credenciais: a data é a da última TROCA de senha (`passwordChangedAt`). */
    passwordSaved: 'Senha cadastrada em {{date}}. Para trocar, digite a nova senha.',
    /** Não recebemos a senha (FR-003), então só sabemos que ela existe. */
    passwordPresent: 'Há uma senha cadastrada. Digite outra para trocar.',
    passwordAbsent: 'Digite a senha que você usa no +Vendas.',
    save: 'Salvar',
    saving: 'Salvando…',
    remove: 'Remover credenciais',
    removing: 'Removendo…',
    removeConfirmTitle: 'Remover as credenciais do +Vendas?',
    removeConfirmHelp: 'Sem elas nenhuma importação pode começar, pela tela ou pelo plugin, até você cadastrar de novo.',
    removeConfirm: 'Remover',
    cancel: 'Cancelar',
    /** FR-007: o +Vendas recusou a senha guardada. O mesmo aviso vale na tela de importação. */
    credentialsNeedUpdate: 'O +Vendas recusou a senha cadastrada. Digite a senha atual para continuar importando.',
    /** Rótulos de `MaisVendasCredentials['status']` (`MAIS_VENDAS_CREDENTIALS_STATUS_KEYS`). */
    credentialsStatus: {
      none: 'Sem credenciais cadastradas',
      valid: 'Credenciais cadastradas',
      needsUpdate: 'Credenciais a atualizar',
    },
    savedToast: 'Credenciais salvas',
    removedToast: 'Credenciais removidas',
    /**
     * Nota de segurança (FR-003/SC-003): o que a vendedora pode esperar da tela. O
     * link "Ir para a configuração" mora em `import.goToSettings`, na tela que
     * precisa dele.
     */
    securityNote: 'A senha fica guardada cifrada no servidor e nunca é exibida, nem mascarada: para trocar, digite a nova.',
  },
  customers: {
    title: 'Clientes',
    searchLabel: 'Buscar cliente',
    empty: 'Nenhum cliente encontrado',
    show: 'Mostrar',
    hide: 'Ocultar',
    cpfLabel: 'CPF',
    purchases: 'Compras',
    netTotal: 'Total líquido',
    lastPurchase: 'Última compra',
    daysSinceLastPurchase: 'Dias desde a última compra',
    noPurchases: 'Sem compras',
    notFound: 'Cliente não encontrado',
    backToList: 'Ver todos os clientes',
    ordersTitle: 'Pedidos do cliente',
    createdOn: 'Cadastro',
    /** `dl` do resumo, para o leitor de tela anunciar de onde vêm os números. */
    summaryAriaLabel: 'Resumo de compras',
    /** Nome longo truncado: o titulo nativo do browser mostra o inteiro. */
    openProfile: 'Abrir perfil de {{name}}',
    /** Perfil estilo rede social: linha "Cliente desde dd/mm/aaaa". */
    since: 'Cliente desde {{date}}',
  },
  orders: {
    title: 'Pedidos',
    sale: 'Venda',
    return: 'Devolução',
    cancellation: 'Cancelamento',
    dateFrom: 'Data inicial',
    dateTo: 'Data final',
    kindLabel: 'Tipo',
    all: 'Todos',
    channelLabel: 'Canal',
    searchLabel: 'Número do pedido ou cliente',
    clearFilters: 'Limpar filtros',
    empty: 'Nenhum pedido encontrado',
    notFound: 'Pedido não encontrado',
    status: 'Status',
    origin: 'Origem',
    originCota: 'Cota',
    originOmni: 'Omni',
    originalSaleOn: 'Venda original',
    cancelledOn: 'Cancelado em',
    items: 'Itens',
    reference: 'Referência',
    size: 'Tamanho',
    price: 'Preço',
    number: 'Número',
    date: 'Data',
    total: 'Total',
    customer: 'Cliente',
    itemsCount_one: '{{count}} item',
    itemsCount_other: '{{count}} itens',
    noImage: 'Sem imagem',
    noItems: 'Pedido sem itens na base',
    backToOrders: 'Ver todos os pedidos',
    openOrder: 'Abrir pedido {{number}}',
    filtersAriaLabel: 'Filtros de pedidos',
  },
  /**
   * Importação do +Vendas (feature 006, tela /importacao). Os status e as ações
   * do relatório usam as chaves do enum da API (`import.status.*`,
   * `import.action.*`, `import.blockStatus.*`), porque o JSON devolve as strings
   * camelCase e a tela traduz por interpolação de chave — nenhum rótulo é
   * montado por código a partir do valor cru.
   */
  import: {
    title: 'Importar do +Vendas',
    subtitle: 'Traga os pedidos de um período. As credenciais do +Vendas ficam na tela de Configurações.',
    nav: 'Importar',
    /**
     * Sem credenciais cadastradas: aviso no lugar do formulário (contracts/ui.md,
     * FR-009). Saem `login` e `password` da tela — o início usa as credenciais
     * guardadas —, então nada aqui transporta senha, nem por URL, nem por storage.
     */
    noCredentials: 'Cadastre as credenciais do +Vendas para importar.',
    goToSettings: 'Ir para a configuração',
    /**
     * Aviso de `credentialsStatus = needsUpdate`: o texto é o da configuração
     * (`settings.credentialsNeedUpdate`) — a tela de importação só muda o destino
     * do link. O iniciar segue disponível: o +Vendas pode aceitar a senha de novo
     * (research R5, FR-007).
     */
    startDate: 'Data inicial',
    endDate: 'Data final',
    dryRun: 'Simular (não gravar)',
    dryRunHelp: 'Mostra o que a importação faria, sem alterar nada.',
    start: 'Iniciar importação',
    starting: 'Iniciando…',
    /**
     * "Atualizar base" (feature 008, FR-013 a FR-015): o período vem calculado do
     * servidor e a confirmação mostra as duas datas antes de iniciar.
     */
    updateBase: 'Atualizar base',
    updateBaseBusy: 'Iniciando…',
    updateBaseConfirm: 'Importar de {{start}} a {{end}}?',
    updateBaseConfirmHelp: 'Vem do último pedido importado até hoje. A simulação não grava nada.',
    updateBaseHelpTitle: 'O que é atualizado?',
    updateBaseHelpBody:
      'A importação percorre o +Vendas do dia do último pedido já importado até hoje. O que já está na base volta como "inalterado"; entram os pedidos novos desse dia e os das datas seguintes. Nada é apagado.',
    updateBaseHelpClose: 'Fechar',
    cancelRun: 'Cancelar',
    refresh: 'Atualizar agora',
    lastImportedHelp: 'A data inicial vem do seu último pedido importado.',
    firstImportHelp: 'Nenhum pedido importado ainda: informe a data inicial.',
    queued: 'Aguardando',
    blockProgress: 'Bloco {{current}} de {{total}}',
    blocksFinished: '{{finished}} de {{total}} blocos concluídos',
    duration: 'Duração',
    durationSeconds: '{{seconds}} s',
    period: 'Período',
    periodRange: '{{start}} a {{end}}',
    modeLabel: 'Modo',
    mode: {
      dryRun: 'Simulação',
      apply: 'Gravação',
    },
    /** Origem do início e o que foi pedido (feature 008, research R8): rótulos de `startedVia`/`kind`. */
    startedViaLabel: 'Iniciada',
    via: {
      screen: 'Pela tela',
      /** A execução apareceu sozinha, pelo polling: dizer a origem evita susto (SC-002). */
      mcp: 'Pelo plugin',
    },
    kindLabel: 'Tipo de importação',
    kind: {
      period: 'Período escolhido',
      updateBase: 'Atualizar base',
    },
    /**
     * Execução que apareceu no polling sem a vendedora ter iniciado (SC-002): a
     * frase diz de onde ela veio, em vez de deixar o progresso parecer um bug.
     */
    startedByPluginHelp: 'Esta importação foi iniciada pelo plugin do ChatGPT e continua aqui.',
    resultLabel: 'Resultado',
    status: {
      queued: 'Aguardando',
      running: 'Em andamento',
      succeeded: 'Concluída',
      failed: 'Falhou',
      partial: 'Parcial',
      cancelled: 'Cancelada',
    },
    blockStatus: {
      pending: 'Pendente',
      running: 'Em andamento',
      succeeded: 'Concluído',
      failed: 'Falhou',
      notExecuted: 'Não executado',
    },
    action: {
      inserted: 'Inserido',
      updated: 'Atualizado',
      unchanged: 'Inalterado',
      missing: 'Não encontrado',
      conflict: 'Conflito com pedido manual',
      error: 'Com erro',
    },
    counts: {
      inserted: 'Inseridos',
      updated: 'Atualizados',
      unchanged: 'Inalterados',
      missing: 'Não encontrados',
      failed: 'Com erro',
      conflicts: 'Conflitos',
      customersCreated: 'Clientes criados',
      imagesStored: 'Fotos gravadas',
      imagesFailed: 'Fotos com erro',
    },
    wouldCounts: {
      inserted: 'Seriam inseridos',
      updated: 'Seriam atualizados',
      unchanged: 'Ficariam inalterados',
      customersCreated: 'Clientes que seriam criados',
      imagesStored: 'Fotos que seriam gravadas',
    },
    simulationTag: 'Simulação — nada foi gravado',
    blocksTitle: 'Blocos',
    blockRow: 'Bloco {{number}}',
    changesTitle: 'Pedidos',
    breakdownTitle: 'Por canal e tipo',
    breakdownRow: '{{channel}} · {{origin}} · {{kind}} · {{action}}: {{count}}',
    originAny: '—',
    ordersWritten_one: '{{count}} pedido gravado',
    ordersWritten_other: '{{count}} pedidos gravados',
    blockOrdersWritten: '{{count}} pedido(s) gravado(s) deste bloco',
    repeatBlock: 'Repetir este bloco',
    changedFields: 'Alterados: {{fields}}',
    openedOrder: 'Abrir pedido {{number}}',
    historyTitle: 'Histórico',
    historyEmpty: 'Nenhuma importação ainda.',
    startedAt: 'Iniciada em',
    countDropWarning: 'Esta execução viu menos pedidos que a anterior do mesmo período ({{previous}}). Confira se o +Vendas respondeu completo.',
    lockMessage: 'Muitas tentativas com credenciais recusadas. Tente novamente às {{time}}.',
    lockedHelp: 'Por segurança, novos inícios ficam bloqueados por alguns minutos depois de várias recusas.',
    startedToast: 'Importação iniciada. Você pode fechar a aba: ela continua no servidor.',
    cancelToast: 'Cancelamento pedido. A execução termina ao fim do bloco atual.',
    progressHelp: 'O progresso atualiza sozinho. Feche e reabra a aba para retomar de onde parou.',
    /**
     * Aviso de `credentialsStatus = needsUpdate` na tela de importação (FR-007). O
     * texto é o mesmo de `settings.credentialsNeedUpdate` — a organização por tela
     * manda repetir a frase (como `common.show`/`customers.show`) para nenhuma
     * tela depender da seção da outra. O iniciar segue disponível: o +Vendas pode
     * aceitar a senha de novo (research R5).
     */
    needsUpdateHelp: 'O +Vendas recusou a senha cadastrada. Digite a senha atual para continuar importando.',
  },
  /**
   * Álbum de fotos da cliente (feature 009, contracts/ui.md). Todas as frases
   * visíveis da grade, da área de inclusão, do visualizador, do editor de
   * recorte e dos avisos de envio (constituição §IV, FR-024).
   */
  customerPhotos: {
    /** Abas do perfil (FR-017). Fotos é a padrão. */
    tabPhotos: 'Fotos',
    tabOrders: 'Pedidos',
    tabsAriaLabel: 'Seções do perfil',
    /** Cabeçalho (contracts/ui.md §1, FR-014/FR-016). */
    adjustProfile: 'Ajustar foto de perfil',
    viewOriginal: 'Ver foto original de {{name}}',
    /** Área de inclusão (FR-020). No toque, `dropTap` (contracts/ui.md §3). */
    dropHere: 'Arraste para incluir uma foto',
    dropTap: 'Toque para incluir uma foto',
    /** Filtro da grade (FR-010, US4-6). */
    filterAll: 'Todas',
    filterFavorites: 'Favoritas',
    filterAriaLabel: 'Filtrar fotos',
    favoriteBadge: 'Favorita',
    /** Marca na célula de que esta foto é a origem do perfil (FR-014). */
    profileBadge: 'Perfil',
    /** `alt` da célula: a descrição da vendedora, ou o nome com a data (FR-018). */
    photoAlt: 'Foto de {{name}} de {{date}}',
    /** Vazia (US1-6). */
    empty: 'Ainda não há fotos de {{name}}.',
    loading: 'Carregando fotos',
    loadingMore: 'Carregando mais fotos',
    /** Estado da fila (FR-021): os dois passos da pesquisa R7. */
    stateUploading: 'enviando',
    stateAttaching: 'registrando',
    stateError: 'Não foi possível incluir',
    dismiss: 'Descartar',
    /** Avisos de recusa local, antes de qualquer requisição (contracts/ui.md §3). */
    rejectedType: '{{name}}: formato não aceito',
    rejectedSize: '{{name}}: acima do limite de {{max}} MB',
    alreadyInAlbum: 'Essa foto já estava no álbum.',
    /** Visualizador (contracts/ui.md §4, FR-019). */
    viewerTitle: 'Foto de {{name}}',
    close: 'Fechar',
    previous: 'Foto anterior',
    next: 'Próxima foto',
    sourceWeb: 'enviada pela tela',
    sourceApi: 'enviada pela API',
    sourceChatGpt: 'enviada pelo ChatGPT',
    favoriteOff: 'Marcar como favorita',
    favoriteOn: 'Desmarcar como favorita',
    favoriteError: 'Não foi possível salvar a favorita.',
    /** Descrição (FR-007, US4-3 a US4-5). */
    descriptionAdd: 'Adicionar descrição',
    descriptionEdit: 'Editar',
    descriptionLabel: 'Descrição',
    descriptionSave: 'Salvar',
    descriptionCancel: 'Cancelar',
    descriptionCount: '{{current}}/{{max}}',
    descriptionTooLong: 'A descrição deve ter no máximo {{max}} caracteres.',
    descriptionError: 'Não foi possível salvar a descrição.',
    /** Perfil a partir da foto (FR-011/FR-012, US3-1). */
    useAsProfile: 'Usar como foto de perfil',
    adjustFrame: 'Ajustar enquadramento',
    /** US3-9: perfil definido por um canal sem navegador. */
    profileWithoutFrame: 'Sem enquadramento dedicado: o círculo usa a própria foto.',
    /** Editor de recorte (contracts/ui.md §5). */
    cropperTitle: 'Enquadramento da foto de perfil',
    zoom: 'Zoom',
    cropSave: 'Salvar',
    cropSaving: 'Salvando…',
    cropCancel: 'Cancelar',
    removeProfile: 'Remover foto de perfil',
    removingProfile: 'Removendo…',
    profileSaved: 'Foto de perfil atualizada.',
    profileRemoved: 'Foto de perfil removida.',
    /** Motivos do recorte feito no navegador (research R4) — nada é processado no servidor. */
    cropErrorNoCanvas: 'Este navegador não consegue desenhar a foto.',
    cropErrorNoToBlob: 'Este navegador não consegue gerar o arquivo do recorte.',
    cropErrorNotLoaded: 'A foto ainda não terminou de carregar.',
    cropErrorBadArea: 'O recorte escolhido não tem tamanho suficiente.',
    cropErrorEmpty: 'Não foi possível gerar o recorte.',
    cropErrorSecurity: 'O armazenamento não liberou a leitura da foto para recortar (CORS).',
    /** R4: o quadrado é gerado no navegador e enviado como arquivo — nada é recortado no servidor. */
    cropperHint: 'O quadrado vira um arquivo de até {{size}} × {{size}}, gerado aqui no navegador.',
    /** Exclusão (FR-009/FR-015, US3-6). */
    delete: 'Excluir',
    deleteTitle: 'Excluir esta foto? Essa ação não pode ser desfeita.',
    deleteProfileExtra: '{{name}} vai voltar a aparecer com as iniciais.',
    deleteConfirm: 'Excluir',
    deleteCancel: 'Cancelar',
    deleting: 'Excluindo…',
  },
  errors: {
    network: 'Não foi possível conectar',
    notFoundPage: 'Página não encontrada',
    unexpected: 'Algo deu errado',
    invalidId: 'Endereço inválido',
  },
} as const
