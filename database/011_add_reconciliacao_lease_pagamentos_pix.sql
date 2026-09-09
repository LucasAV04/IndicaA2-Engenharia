-- UP: lease persistente de uma reconciliação de PagamentoPix por vez.
ALTER TABLE pagamentos_pix
    ADD COLUMN reconciliacao_lease_id CHAR(36) NULL,
    ADD COLUMN reconciliacao_lease_expira_em DATETIME(6) NULL,
    ADD INDEX ix_pagamentos_pix_reconciliacao_lease (reconciliacao_lease_expira_em);

-- DOWN: execute somente ao reverter esta migration, após assegurar que não há reconciliação ativa.
-- ALTER TABLE pagamentos_pix
--     DROP INDEX ix_pagamentos_pix_reconciliacao_lease,
--     DROP COLUMN reconciliacao_lease_expira_em,
--     DROP COLUMN reconciliacao_lease_id;
