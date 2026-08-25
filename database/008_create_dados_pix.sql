CREATE TABLE IF NOT EXISTS dados_pix (
    id CHAR(36) NOT NULL,
    usuario_id CHAR(36) NOT NULL,
    tipo_chave_pix INT NOT NULL,
    chave_pix_ciphertext BLOB NOT NULL,
    chave_pix_nonce VARBINARY(12) NOT NULL,
    chave_pix_tag VARBINARY(16) NOT NULL,
    encryption_version INT NOT NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT pk_dados_pix PRIMARY KEY (id),
    CONSTRAINT uq_dados_pix_usuario_id UNIQUE (usuario_id),
    CONSTRAINT fk_dados_pix_usuarios FOREIGN KEY (usuario_id) REFERENCES usuarios (id)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;
