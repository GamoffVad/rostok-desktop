-- База данных «Ростка». Каждая запись принадлежит рабочему пространству сотрудника (workspace_id):
-- сотрудники в локальной сети работают с одним файлом, но видят только свои группы, детей и настройки.
-- Журнал — DELETE (режим WAL не работает в общей папке сети).

CREATE TABLE IF NOT EXISTS meta (
  key   TEXT PRIMARY KEY,
  value TEXT
);

-- пользователи: вход по логину и паролю (соль и хеш PBKDF2-SHA256)
CREATE TABLE IF NOT EXISTS users (
  id             TEXT PRIMARY KEY,
  login          TEXT NOT NULL,
  name           TEXT NOT NULL DEFAULT '',
  role           TEXT NOT NULL DEFAULT 'user', -- main_admin | admin | user
  password_hash  TEXT NOT NULL,
  password_salt  TEXT NOT NULL,
  iterations     INTEGER NOT NULL,
  created_at     TEXT NOT NULL,
  last_login_at  TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_login ON users (login COLLATE NOCASE);

-- рабочие пространства (хранилища) принадлежат пользователям; у базы прежних версий колонки owner_id нет — её добавляет миграция
CREATE TABLE IF NOT EXISTS workspaces (
  id             TEXT PRIMARY KEY,
  name           TEXT NOT NULL,
  owner_id       TEXT,
  created_at     TEXT NOT NULL,
  last_opened_at TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_workspaces_name ON workspaces (name COLLATE NOCASE);

CREATE TABLE IF NOT EXISTS groups (
  workspace_id TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
  id           TEXT NOT NULL,
  name         TEXT NOT NULL,
  sort         INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (workspace_id, id)
);

CREATE TABLE IF NOT EXISTS children (
  workspace_id TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
  id           TEXT NOT NULL,
  group_id     TEXT NOT NULL,
  name         TEXT NOT NULL,
  birth_date   TEXT NOT NULL DEFAULT '',
  note         TEXT NOT NULL DEFAULT '',
  tpmpk        TEXT NOT NULL DEFAULT '',
  relatives    TEXT NOT NULL DEFAULT '[]', -- родители и родственники, JSON
  PRIMARY KEY (workspace_id, id)
);
CREATE INDEX IF NOT EXISTS ix_children_group ON children (workspace_id, group_id);

CREATE TABLE IF NOT EXISTS periods (
  workspace_id TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
  id           TEXT NOT NULL,
  year         TEXT NOT NULL,
  point        TEXT NOT NULL,
  sort         INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (workspace_id, id)
);

CREATE TABLE IF NOT EXISTS scores (
  workspace_id TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
  child_id     TEXT NOT NULL,
  period_id    TEXT NOT NULL,
  item_id      TEXT NOT NULL,
  value        TEXT NOT NULL,
  PRIMARY KEY (workspace_id, child_id, period_id, item_id)
);

CREATE TABLE IF NOT EXISTS notes (
  workspace_id TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
  child_id     TEXT NOT NULL,
  period_id    TEXT NOT NULL,
  text         TEXT NOT NULL,
  PRIMARY KEY (workspace_id, child_id, period_id)
);

CREATE TABLE IF NOT EXISTS programs (
  workspace_id TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
  child_id     TEXT NOT NULL,
  period_id    TEXT NOT NULL,
  data         TEXT NOT NULL, -- порог, исключённые пробы, дополнения, рекомендации — JSON
  PRIMARY KEY (workspace_id, child_id, period_id)
);

CREATE TABLE IF NOT EXISTS library (
  workspace_id TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
  item_id      TEXT NOT NULL,
  text         TEXT NOT NULL,
  PRIMARY KEY (workspace_id, item_id)
);

-- настройки рабочего пространства: выбранные группа, срез и раздел, правки словарей, признак своей библиотеки
CREATE TABLE IF NOT EXISTS settings (
  workspace_id TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE,
  key          TEXT NOT NULL,
  value        TEXT NOT NULL,
  PRIMARY KEY (workspace_id, key)
);

-- журнал: когда администратор открывал чужое рабочее пространство
CREATE TABLE IF NOT EXISTS access_log (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  workspace_id TEXT NOT NULL,
  viewer_id    TEXT NOT NULL,
  viewer_name  TEXT NOT NULL,
  action       TEXT NOT NULL, -- view (password_reset — записи версии 1.1)
  at           TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_access_log_workspace ON access_log (workspace_id, at);
