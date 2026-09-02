CREATE TABLE IF NOT EXISTS operacoes_pagamento_pix (
    id CHAR(36) NOT NULL,
    pagamento_pix_id CHAR(36) NOT NULL,
    tipo_operacao INT NOT NULL,
    numero_tentativa_envio INT NULL,
    referencia_idempotente CHAR(32) NOT NULL,
    resultado INT NULL,
    identificador_provider VARCHAR(255) NULL,
    codigo VARCHAR(255) NULL,
    started_at DATETIME(6) NOT NULL,
    finished_at DATETIME(6) NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT pk_operacoes_pagamento_pix PRIMARY KEY (id),
    CONSTRAINT fk_operacoes_pagamento_pix_pagamentos_pix
        FOREIGN KEY (pagamento_pix_id) REFERENCES pagamentos_pix (id),
    CONSTRAINT uq_operacoes_pagamento_pix_envio_tentativa
        UNIQUE (pagamento_pix_id, numero_tentativa_envio),
    INDEX ix_operacoes_pagamento_pix_pagamento_started (pagamento_pix_id, started_at),
    INDEX ix_operacoes_pagamento_pix_abertas (finished_at, started_at)
);
