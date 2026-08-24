CREATE TABLE IF NOT EXISTS cashbacks (
    id CHAR(36) NOT NULL,
    indicacao_id CHAR(36) NOT NULL,
    pagamento_vistoria_id CHAR(36) NOT NULL,
    usuario_indicador_id CHAR(36) NOT NULL,
    valor_total_pago DECIMAL(12,2) NOT NULL,
    percentual DECIMAL(5,4) NOT NULL,
    valor DECIMAL(12,2) NOT NULL,
    status INT NOT NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT pk_cashbacks PRIMARY KEY (id),
    CONSTRAINT uq_cashbacks_pagamento_vistoria_id UNIQUE (pagamento_vistoria_id),
    CONSTRAINT fk_cashbacks_indicacoes FOREIGN KEY (indicacao_id) REFERENCES indicacoes (id),
    CONSTRAINT fk_cashbacks_pagamentos_vistoria FOREIGN KEY (pagamento_vistoria_id) REFERENCES pagamentos_vistoria (id),
    CONSTRAINT fk_cashbacks_usuarios_indicadores FOREIGN KEY (usuario_indicador_id) REFERENCES usuarios (id)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;
