# Contribuição

- Desenvolva em incrementos com escopo claro. Faça commits coerentes em inglês, seguindo [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/).
- Separe responsabilidades. Mantenha invariantes no domínio, contratos de casos de uso na aplicação, detalhes de persistência e integrações na infraestrutura e preocupações de transporte HTTP na API.
- Prefira português no código de domínio, preservando terminologia técnica consagrada e contratos existentes. Use nomes claros, evite duplicar regras e adie abstrações até haver uma necessidade concreta.
- Use comentários para registrar decisões, invariantes e restrições técnicas.
- Revise segurança e concorrência na medida do risco. Cubra cenários relevantes com testes e use integração real quando o comportamento depender do banco, do servidor HTTP ou de outra infraestrutura.
- Preserve contratos e dados. Quando uma mudança intencional afetá-los, documente o impacto para consumidores e a transição dos dados.
- Use IA como apoio. Verifique as sugestões e mantenha a responsabilidade pelo resultado. Mantenha credenciais, prompts, logs temporários e artefatos desnecessários fora do repositório.

## Definition of Done

- [ ] O incremento atende aos critérios de aceitação e a documentação corresponde ao comportamento entregue.
- [ ] Os impactos em contratos e dados foram verificados, e os riscos de segurança e concorrência foram tratados conforme o escopo.
- [ ] O build Release, os testes pertinentes e o CI passaram. A integração real foi exercitada quando necessária.
- [ ] O diff completo e os commits foram revisados. `git diff --check` passou e o conteúdo versionado está livre de credenciais e artefatos temporários.
- [ ] Uma pessoa revisora examinou a alteração e registrou a aprovação definitiva. Avaliações automatizadas ou por IA servem de apoio à revisão humana obrigatória.
