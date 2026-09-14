-- UP: lease persistente do executor de Envio Pix. Interrompa executores antigos
-- antes de aplicar ou reverter esta migration, pois versões anteriores não validam token.
ALTER TABLE pagamentos_pix
    ADD COLUMN envio_lease_id CHAR(36) NULL,
    ADD COLUMN envio_lease_expira_em DATETIME(6) NULL;

-- DOWN: execute somente após assegurar que não há Envio Pix ativo.
-- ALTER TABLE pagamentos_pix
--     DROP COLUMN envio_lease_expira_em,
--     DROP COLUMN envio_lease_id;
