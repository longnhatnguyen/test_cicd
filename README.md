# Tiny MVC Demo With GitHub Actions + Docker

Demo nho de hoc luong `GitHub -> GitHub Actions -> GHCR -> Docker -> ASP.NET MVC`.

## Thanh phan

- `TinyMvcDemo/`: ung dung ASP.NET Core MVC
- `.github/workflows/deploy.yml`: pipeline build, push image va deploy
- `deploy/docker-compose.server.yml`: compose file chay tren server
- `TinyMvcDemo/Dockerfile`: image app

## Flow CI/CD

1. Push code len nhanh `main`
2. GitHub Actions chay `dotnet restore` va `dotnet build`
3. Workflow build Docker image
4. Push image len `GHCR`
5. Workflow SSH vao server
6. Server `docker compose pull` va `docker compose up -d`

## Cau hinh GitHub Secrets

Tao cac secret sau trong repo:

- `SERVER_HOST`: IP hoac domain server
- `SERVER_PORT`: cong SSH, thuong la `22`
- `SERVER_USER`: user dung de deploy
- `SERVER_SSH_PASSWORD`: mat khau SSH cua user deploy
- `GHCR_READ_TOKEN`: token co quyen read package tren GHCR

`GHCR_READ_TOKEN` nen la Personal Access Token cua GitHub voi quyen:

- `read:packages`

Ban co the copy mau trong file:

- `.github/github-secrets-template.md`

Neu repo la private, user/robot dung token nay cung nen co quyen doc repo.

## Chuan bi server mot lan

Server can co:

- Docker Engine
- Docker Compose plugin
- Git

Clone repo len server:

```bash
sudo mkdir -p /opt/test_cicd
sudo chown -R $USER:$USER /opt/test_cicd
git clone https://github.com/longnhatnguyen/test_cicd.git /opt/test_cicd
```

Neu user deploy chua co quyen chay Docker:

```bash
sudo usermod -aG docker $USER
newgrp docker
```

Mo cong app neu can:

```bash
sudo ufw allow 5055/tcp
```

## Deploy lan dau thu cong tren server

Dang nhap GHCR:

```bash
echo "<GHCR_READ_TOKEN>" | docker login ghcr.io -u <github-username> --password-stdin
```

Chay app:

```bash
cd /opt/test_cicd
export IMAGE_REPO=ghcr.io/longnhatnguyen/test_cicd
export IMAGE_TAG=latest
docker compose -f deploy/docker-compose.server.yml up -d
```

Sau do app se len o:

- `http://<server-ip>:5055`

## Cach test CI/CD

1. Sua file `TinyMvcDemo/Views/Home/Index.cshtml`
2. Commit va push len `main`

```bash
git add .
git commit -m "Update home page message"
git push
```

3. Vao tab `Actions` tren GitHub de xem pipeline
4. Refresh app tren server

Ban se thay:

- `Commit` doi theo commit moi
- `Build number` doi theo GitHub Actions run number
- `Deployed at` doi theo lan deploy moi

## Ghi chu

- `docker-compose.yml`, `Dockerfile.jenkins`, `Jenkinsfile` la file tu demo Jenkins cu, hien khong can cho flow GitHub Actions.
- Neu muon, ban co the xoa cac file Jenkins sau khi da chay on pipeline moi.
