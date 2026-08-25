CREATE TABLE IF NOT EXISTS pagamentos_pix (
    id CHAR(36) NOT NULL,
    cashback_id CHAR(36) NOT NULL,
    usuario_beneficiario_id CHAR(36) NOT NULL,
    valor DECIMAL(12,2) NOT NULL,
    tipo_chave_pix INT NOT NULL,
    chave_pix_ciphertext BLOB NOT NULL,
    chave_pix_nonce VARBINARY(12) NOT NULL,
    chave_pix_tag VARBINARY(16) NOT NULL,
    encryption_version INT NOT NULL,
    status INT NOT NULL,
    quantidade_tentativas INT NOT NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT pk_pagamentos_pix PRIMARY KEY (id),
    CONSTRAINT uq_pagamentos_pix_cashback_id UNIQUE (cashback_id),
    CONSTRAINT fk_pagamentos_pix_cashbacks FOREIGN KEY (cashback_id) REFERENCES cashbacks (id),
    CONSTRAINT fk_pagamentos_pix_usuarios_beneficiarios FOREIGN KEY (usuario_beneficiario_id) REFERENCES usuarios (id)
);
