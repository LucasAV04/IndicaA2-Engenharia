CREATE TABLE IF NOT EXISTS usuarios (
    id CHAR(36) NOT NULL,
    nome VARCHAR(150) NOT NULL,
    email VARCHAR(254) NOT NULL,
    senha_hash VARCHAR(255) NOT NULL,
    telefone VARCHAR(30) NULL,
    status INT NOT NULL,
    tipo_usuario INT NOT NULL,
    email_confirmado BOOLEAN NOT NULL,
    ultimo_login DATETIME(6) NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT pk_usuarios PRIMARY KEY (id),
    CONSTRAINT uq_usuarios_email UNIQUE (email)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;
