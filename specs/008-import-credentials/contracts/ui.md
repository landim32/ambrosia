# Contrato: telas (008)

## Configuração do +Vendas — `/configuracoes/mais-vendas` (nova)

- **Acesso**: autenticada; link no menu ("Configurações") e na tela de importação.
- **Campos**: Login do +Vendas (texto, preenchido com o login cadastrado); Senha (`type="password"`, `autocomplete="new-password"`, sempre vazia ao abrir).
- **Estado sem credenciais**: texto "Cadastre o login e a senha que você usa no +Vendas. A senha fica guardada protegida e nunca é exibida." Botão **Salvar**.
- **Estado com credenciais**: abaixo da senha, "Senha cadastrada em dd/mm/aaaa. Para trocar, digite a nova senha." Botões **Salvar** e **Remover credenciais** (abre modal de confirmação).
- **Credenciais a atualizar** (`status = needsUpdate`): alerta "O +Vendas recusou a senha cadastrada. Digite a senha atual para continuar importando."
- **Validação**: login obrigatório; senha obrigatória sem credenciais ou ao trocar o login ("Informe a senha para trocar o login."). Depois de salvar, o campo de senha é limpo e aparece o toast "Credenciais salvas".
- **Nunca**: mostrar a senha, nem mascarada; botão de "mostrar senha" só para o que a vendedora está digitando.

## Importação — `/importacao` (alterada)

- **Saem**: campos de login e senha do +Vendas.
- **Sem credenciais**: no lugar do formulário, aviso "Cadastre as credenciais do +Vendas para importar." com botão **Ir para a configuração**; iniciar fica indisponível.
- **Credenciais a atualizar**: aviso com link para a configuração; iniciar continua disponível.
- **Formulário**: data inicial, data final, simulação e **Iniciar importação**. Validação de período com o limite de 3 meses ("O período não pode passar de 3 meses."), a partir de `maxRangeMonths` dos defaults.
- **Atualizar base**: botão **Atualizar base** ao lado do iniciar. Habilitado quando `updateBase.available`; ao clicar, abre modal de confirmação "Importar de dd/mm/aaaa a dd/mm/aaaa?" com a opção de simulação. Indisponível: botão desabilitado com o `reason` como dica.
- **Restante** (progresso, relatório, histórico, cancelamento, `?inicio=&fim=`) igual à feature 006.
