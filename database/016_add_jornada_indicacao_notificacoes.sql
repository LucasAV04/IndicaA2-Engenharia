ALTER TABLE indicacoes
    ADD COLUMN origem INT NOT NULL DEFAULT 0,
    ADD COLUMN consentimento_versao VARCHAR(32) NULL,
    ADD COLUMN consentimento_em DATETIME(6) NULL,
    ADD COLUMN idempotencia_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD COLUMN protocolo_publico CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD CONSTRAINT uq_indicacoes_idempotencia UNIQUE (idempotencia_hash),
    ADD CONSTRAINT uq_indicacoes_protocolo UNIQUE (protocolo_publico),
    ADD CONSTRAINT ck_indicacoes_origem CHECK (origem IN (0,1)),
    ADD CONSTRAINT ck_indicacoes_consentimento CHECK (origem=0 OR
        (consentimento_versao IS NOT NULL AND CHAR_LENGTH(consentimento_versao)>0
         AND consentimento_em IS NOT NULL AND idempotencia_hash IS NOT NULL AND protocolo_publico IS NOT NULL)),
    ADD INDEX ix_indicacoes_portal (usuario_indicador_id,created_at,id);

CREATE TABLE notificacoes_internas (
    id CHAR(36) NOT NULL PRIMARY KEY,
    tipo INT NOT NULL,
    escopo INT NOT NULL,
    usuario_id CHAR(36) NULL,
    referencia_id CHAR(36) NOT NULL,
    evento_chave VARCHAR(180) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME(6) NOT NULL,
    lida_em DATETIME(6) NULL,
    CONSTRAINT uq_notificacoes_evento UNIQUE (evento_chave),
    CONSTRAINT fk_notificacoes_usuario FOREIGN KEY (usuario_id) REFERENCES usuarios(id),
    CONSTRAINT ck_notificacoes_tipo CHECK (tipo BETWEEN 0 AND 6),
    CONSTRAINT ck_notificacoes_escopo CHECK ((escopo=0 AND usuario_id IS NOT NULL) OR (escopo=1 AND usuario_id IS NULL)),
    INDEX ix_notificacoes_destinatario (escopo,usuario_id,lida_em,created_at,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX ix_cashbacks_preparacao ON cashbacks(status,created_at,id);
