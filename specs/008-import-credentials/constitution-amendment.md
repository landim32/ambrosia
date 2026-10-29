# Proposta de emenda à constituição: credenciais de serviços de terceiros

**Versão**: 3.4.0 → 3.4.1 (PATCH: esclarece regra existente, sem princípio novo)
**Princípio afetado**: VI. Autenticação e Segurança
**Motivo**: a feature 008 passa a guardar login e senha do +Vendas para iniciar importações sem a vendedora redigitá-los. A constituição regula senhas **do Ambrosio** (hash lento, nunca recuperável) e dados pessoais (hash + cifra), mas não credenciais de terceiros, que precisam voltar a texto claro para serem usadas.

## Texto a acrescentar (Regras de segurança, Princípio VI)

> - Credenciais de serviços de terceiros que o sistema precisa usar em nome do usuário (ex.: login e senha do +Vendas) DEVEM ser gravadas apenas com cifra autenticada (AES-256-GCM ou mais forte), com chave **dedicada** vinda de variável de ambiente (a API e os hosts que as usam DEVEM recusar o startup sem ela) e dados associados que amarrem a cifra ao usuário e ao campo. A senha de terceiro NUNCA DEVE ser devolvida por endpoint, tool, tela, log, relatório ou auditoria — nem mascarada — e só DEVE ser decifrada no instante de uso. Nenhuma tool do MCP pode receber, ler ou alterar credenciais de terceiros.

## Sync Impact Report

- Versão: 3.4.0 → 3.4.1.
- Seções modificadas: Princípio VI (Regras de segurança).
- Templates: sem mudança.
- Specs afetadas: 006 (FR-005/FR-006, "nenhuma credencial gravada", substituídos pela 008); 007 (R9, importação por link, substituído pelo R9 da 008).
