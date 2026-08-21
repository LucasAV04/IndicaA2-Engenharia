CREATE TABLE IF NOT EXISTS pagamentos_vistoria (
    id CHAR(36) NOT NULL,
    vistoria_id CHAR(36) NOT NULL,
    valor DECIMAL(12,2) NOT NULL,
    status INT NOT NULL,
    pago_em DATETIME(6) NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT pk_pagamentos_vistoria PRIMARY KEY (id),
    CONSTRAINT fk_pagamentos_vistoria_vistorias FOREIGN KEY (vistoria_id) REFERENCES vistorias (id),
    CONSTRAINT uq_pagamentos_vistoria_vistoria_id UNIQUE (vistoria_id)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;
