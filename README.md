# Tiny MVC Demo With Jenkins

Demo nho de hoc luong `GitHub -> Jenkins -> Docker -> ASP.NET MVC`.

## Thanh phan

- `TinyMvcDemo/`: ung dung ASP.NET Core MVC
- `docker-compose.yml`: chay Jenkins tren may local
- `Dockerfile.jenkins`: image Jenkins co san Docker CLI
- `Jenkinsfile`: pipeline build va deploy app

## URL sau khi chay

- Jenkins: `http://localhost:8088`
- Demo app: `http://localhost:5055`

## Trang thai hien tai tren may nay

- Jenkins da chay bang Docker tren host local
- Demo app da chay o `http://localhost:5055`
- Jenkins initial admin password nam trong file:
  `jenkins_home/secrets/initialAdminPassword`

## Tao repo GitHub

1. Dang nhap GitHub va tao repo moi, vi du `tiny-mvc-jenkins-demo`
2. Khong can chon them README hay `.gitignore` tren GitHub, vi code da co san local
3. Sau khi repo duoc tao, chay cac lenh sau trong thu muc nay:

```bash
git init -b main
git add .
git commit -m "Initial tiny MVC demo with Jenkins"
git remote add origin https://github.com/<tai-khoan>/<ten-repo>.git
git push -u origin main
```

## Cau hinh Jenkins lan dau

1. Mo `http://localhost:8088`
2. Dang nhap bang mat khau initial admin trong:
   `jenkins_home/secrets/initialAdminPassword`
3. Chon `Install suggested plugins`
4. Tao tai khoan admin cho ban
5. Sau khi vao dashboard, tao job moi:
   - Chon `New Item`
   - Dat ten: `tiny-mvc-demo`
   - Chon `Pipeline`

## Cau hinh job de lay code tu GitHub

Trong job `tiny-mvc-demo`:

1. Vao `Configure`
2. O muc `Pipeline`, chon `Pipeline script from SCM`
3. `SCM` = `Git`
4. `Repository URL` = URL repo GitHub cua ban
5. `Branch Specifier` = `*/main`
6. `Script Path` = `Jenkinsfile`
7. Tick `GitHub hook trigger for GITScm polling` neu sau nay ban co public webhook
8. Trong `Build Triggers`, tick `Poll SCM` va dung lich:

```text
H/2 * * * *
```

Dieu nay co nghia la Jenkins se tu kiem tra repo moi 2 phut mot lan. Vi may host cua ban dang local, day la cach don gian nhat de demo ngay ma khong can public IP hay reverse proxy.

## Cach test thay doi

1. Sua file `TinyMvcDemo/Views/Home/Index.cshtml`
2. Hoac doi noi dung `DEMO_MESSAGE` trong `Jenkinsfile`
3. Chay:

```bash
git add .
git commit -m "Update home page message"
git push
```

4. Cho Jenkins poll repo va build lai
5. Refresh `http://localhost:5055`

Ban se thay:

- thong diep tren trang doi theo code moi
- `Build number` thay doi
- `Commit` thay doi
- `Deployed at` thay doi
