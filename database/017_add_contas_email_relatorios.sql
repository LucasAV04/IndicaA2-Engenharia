ALTER TABLE usuarios
    ADD COLUMN versao_credenciais BIGINT NOT NULL DEFAULT 1,
    ADD CONSTRAINT ck_usuarios_versao_credenciais CHECK (versao_credenciais >= 1);

CREATE TABLE tokens_conta (
    id CHAR(36) NOT NULL PRIMARY KEY,
    usuario_id CHAR(36) NOT NULL,
    finalidade TINYINT NOT NULL,
    token_hash BINARY(32) NOT NULL,
    email_hash BINARY(32) NOT NULL,
    criado_em DATETIME(6) NOT NULL,
    expira_em DATETIME(6) NOT NULL,
    consumido_em DATETIME(6) NULL,
    revogado_em DATETIME(6) NULL,
    ativo TINYINT GENERATED ALWAYS AS
        (CASE WHEN consumido_em IS NULL AND revogado_em IS NULL THEN 1 ELSE NULL END) STORED,
    CONSTRAINT fk_tokens_conta_usuario FOREIGN KEY (usuario_id) REFERENCES usuarios(id) ON DELETE RESTRICT,
    CONSTRAINT uq_tokens_conta_hash UNIQUE (token_hash),
    CONSTRAINT uq_tokens_conta_ativo UNIQUE (usuario_id, finalidade, ativo),
    CONSTRAINT ck_tokens_conta_finalidade CHECK (finalidade IN (0,1)),
    CONSTRAINT ck_tokens_conta_validade CHECK (expira_em > criado_em),
    CONSTRAINT ck_tokens_conta_uso CHECK (consumido_em IS NULL OR revogado_em IS NULL),
    INDEX ix_tokens_conta_solicitacao (usuario_id, finalidade, criado_em)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE email_outbox (
    id CHAR(36) NOT NULL PRIMARY KEY,
    usuario_id CHAR(36) NOT NULL,
    template TINYINT NOT NULL,
    referencia_id CHAR(36) NOT NULL,
    canal TINYINT NOT NULL DEFAULT 0,
    token_id CHAR(36) NULL,
    token_cifrado VARBINARY(256) NULL,
    nonce BINARY(12) NULL,
    tag BINARY(16) NULL,
    versao_cifra TINYINT NULL,
    status TINYINT NOT NULL DEFAULT 0,
    tentativa TINYINT NOT NULL DEFAULT 0,
    proxima_tentativa_em DATETIME(6) NOT NULL,
    executor_id CHAR(36) NULL,
    lease_expira_em DATETIME(6) NULL,
    criado_em DATETIME(6) NOT NULL,
    enviado_em DATETIME(6) NULL,
    falhou_em DATETIME(6) NULL,
    erro TINYINT NULL,
    CONSTRAINT fk_email_outbox_usuario FOREIGN KEY (usuario_id) REFERENCES usuarios(id) ON DELETE RESTRICT,
    CONSTRAINT fk_email_outbox_token FOREIGN KEY (token_id) REFERENCES tokens_conta(id) ON DELETE RESTRICT,
    CONSTRAINT uq_email_outbox_evento UNIQUE (template, referencia_id, usuario_id, canal),
    CONSTRAINT ck_email_outbox_template CHECK (template BETWEEN 0 AND 6 AND canal = 0),
    CONSTRAINT ck_email_outbox_token CHECK ((template IN (0,1) AND token_id IS NOT NULL) OR
        (template BETWEEN 2 AND 6 AND token_id IS NULL)),
    CONSTRAINT ck_email_outbox_status CHECK (status BETWEEN 0 AND 5 AND tentativa BETWEEN 0 AND 5),
    CONSTRAINT ck_email_outbox_lease CHECK (
        (status = 1 AND executor_id IS NOT NULL AND lease_expira_em IS NOT NULL) OR
        (status <> 1 AND executor_id IS NULL AND lease_expira_em IS NULL)),
    CONSTRAINT ck_email_outbox_cifra CHECK (
        (token_cifrado IS NULL AND nonce IS NULL AND tag IS NULL AND versao_cifra IS NULL) OR
        (token_id IS NOT NULL AND token_cifrado IS NOT NULL AND nonce IS NOT NULL AND tag IS NOT NULL
         AND versao_cifra IS NOT NULL AND versao_cifra = 1 AND status IN (0,1,4))),
    INDEX ix_email_outbox_proximo (status, proxima_tentativa_em, id),
    INDEX ix_email_outbox_lease (status, lease_expira_em, id),
    INDEX ix_email_outbox_relatorio (criado_em, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE notificacoes_internas ADD COLUMN elegivel_email BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE notificacoes_internas ALTER COLUMN elegivel_email SET DEFAULT TRUE;
CREATE INDEX ix_notificacoes_email ON notificacoes_internas(elegivel_email,escopo,created_at,id);
