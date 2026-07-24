# GitHub Secrets Template

Tao cac secret sau trong repo `Settings -> Secrets and variables -> Actions`.

## Secrets can tao

```text
SERVER_HOST=
SERVER_PORT=
SERVER_USER=
SERVER_SSH_PASSWORD=
GHCR_READ_TOKEN=
```

## Y nghia

- `SERVER_HOST`: IP hoac domain server
- `SERVER_PORT`: cong SSH, thuong la `22`
- `SERVER_USER`: user dung de deploy
- `SERVER_SSH_PASSWORD`: mat khau SSH cua user deploy
- `GHCR_READ_TOKEN`: token GitHub co quyen `read:packages`

## Ghi chu

- `SERVER_SSH_PASSWORD` la mat khau dang dung de SSH vao server
- `GHCR_READ_TOKEN` nen la Personal Access Token co quyen `read:packages`
- Khong commit gia tri that cua secrets vao repo
