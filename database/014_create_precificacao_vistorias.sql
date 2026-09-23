-- UP: sem seeds, backfill ou conversão dos textos históricos.
CREATE TABLE tipos_planta (
    id CHAR(36) NOT NULL PRIMARY KEY,
    nome VARCHAR(150) NOT NULL,
    nome_normalizado VARCHAR(150) COLLATE utf8mb4_bin NOT NULL,
    ativo BOOLEAN NOT NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT uq_tipos_planta_nome UNIQUE (nome_normalizado),
    INDEX ix_tipos_planta_ativo_nome (ativo, nome)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE precos_vistoria (
    id CHAR(36) NOT NULL PRIMARY KEY,
    tipo_planta_id CHAR(36) NOT NULL,
    nome_tipo_planta VARCHAR(150) NOT NULL,
    preco_m2 DECIMAL(12,4) NOT NULL,
    modalidade INT NOT NULL,
    acrescimo DECIMAL(12,4) NOT NULL,
    versao INT NOT NULL,
    ativo BOOLEAN NOT NULL,
    tipo_ativo CHAR(36) GENERATED ALWAYS AS (CASE WHEN ativo = 1 THEN tipo_planta_id ELSE NULL END) STORED,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    desativado_em DATETIME(6) NULL,
    CONSTRAINT fk_precos_tipo FOREIGN KEY (tipo_planta_id) REFERENCES tipos_planta(id),
    CONSTRAINT uq_precos_tipo_versao UNIQUE (tipo_planta_id, versao),
    CONSTRAINT uq_precos_tipo_ativo UNIQUE (tipo_ativo),
    INDEX ix_precos_criacao (created_at, id),
    INDEX ix_precos_ativos (ativo, nome_tipo_planta),
    CONSTRAINT ck_precos_valores CHECK (preco_m2 > 0 AND acrescimo >= 0 AND versao > 0
        AND modalidade IN (0,1) AND (modalidade = 0 OR acrescimo <= 10000)),
    CONSTRAINT ck_precos_situacao CHECK ((ativo = 1 AND desativado_em IS NULL) OR (ativo = 0 AND desativado_em IS NOT NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE vistorias
    ADD COLUMN tipo_planta_id CHAR(36) NULL,
    ADD COLUMN preco_vistoria_id CHAR(36) NULL,
    ADD COLUMN preco_versao INT NULL,
    ADD COLUMN preco_m2 DECIMAL(12,4) NULL,
    ADD COLUMN preco_modalidade INT NULL,
    ADD COLUMN preco_acrescimo DECIMAL(12,4) NULL,
    ADD COLUMN valor_base DECIMAL(24,6) NULL,
    ADD COLUMN valor_final DECIMAL(12,2) NULL,
    ADD COLUMN calculado_em DATETIME(6) NULL,
    ADD CONSTRAINT fk_vistorias_tipo_planta FOREIGN KEY (tipo_planta_id) REFERENCES tipos_planta(id),
    ADD CONSTRAINT fk_vistorias_preco FOREIGN KEY (preco_vistoria_id) REFERENCES precos_vistoria(id),
    ADD CONSTRAINT ck_vistorias_snapshot CHECK (
        (tipo_planta_id IS NULL AND preco_vistoria_id IS NULL AND preco_versao IS NULL
         AND preco_m2 IS NULL AND preco_modalidade IS NULL AND preco_acrescimo IS NULL
         AND valor_base IS NULL AND valor_final IS NULL AND calculado_em IS NULL)
        OR
        (tipo_planta_id IS NOT NULL AND preco_vistoria_id IS NOT NULL AND preco_versao IS NOT NULL
         AND preco_m2 IS NOT NULL AND preco_modalidade IS NOT NULL AND preco_acrescimo IS NOT NULL
         AND valor_base IS NOT NULL AND valor_final IS NOT NULL AND calculado_em IS NOT NULL
         AND preco_versao > 0 AND preco_m2 > 0 AND preco_modalidade IN (0,1)
         AND preco_acrescimo >= 0 AND valor_base > 0 AND valor_final > 0));

-- DOWN não automático: snapshots são histórico e não devem ser descartados.
