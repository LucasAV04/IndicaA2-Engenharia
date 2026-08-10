CREATE TABLE IF NOT EXISTS indicacoes (
    id CHAR(36) NOT NULL,
    usuario_indicador_id CHAR(36) NOT NULL,
    usuario_indicado_id CHAR(36) NULL,
    nome_indicada VARCHAR(150) NOT NULL,
    telefone_indicada VARCHAR(30) NOT NULL,
    codigo_indicacao_usado VARCHAR(100) NOT NULL,
    vistoria_id CHAR(36) NULL,
    status INT NOT NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT pk_indicacoes PRIMARY KEY (id),
    INDEX ix_indicacoes_usuario_indicador_id (usuario_indicador_id),
    INDEX ix_indicacoes_status (status),
    INDEX ix_indicacoes_vistoria_id (vistoria_id)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;
