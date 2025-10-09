# BudgetControlSite
Cursorで予実管理サイトを構築してみる


中身の保証はできません。あくまでCursorのテストです。

ディレクトリ直下(/BudgetControlSite)でdockerを立ち上げた後に
/TaskManagementApp上でMigrationファイルからDBを作成してください。

```
cd BudgetControlSite
docker compose up -d

cd TaskManagementApp
dotnet ef database update
```

起動したDBには最初のUserアカウントがないので、
以下SQLでアカウントを追加してください。

```
insert into public."Users"("Name","Email","PasswordHash","Role") values 
    ('admin','admin@example.com','AQAAAAIAAYagAAAAEANAWKEr/yJR7j4mh/M8MCjsGlRFgl6Zx215R4q5T8UQMiVanyn9rt37EBTokeGKvQ==','Admin');
```

アプリを起動します。
```
dotnet run
```

/pics の写真のようなWebアプリが起動します。

