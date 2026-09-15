-- UP: otimiza exclusivamente a seleção ordenada e limitada de candidatos do worker.
CREATE INDEX idx_pagamentos_pix_processamento
    ON pagamentos_pix (status, updated_at, id);

-- DOWN: execute somente após confirmar que nenhum plano de consulta depende deste índice.
-- DROP INDEX idx_pagamentos_pix_processamento ON pagamentos_pix;
