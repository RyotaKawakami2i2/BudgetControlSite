# 同梱データの出典

## common-passwords.txt.gz

頻出パスワードの一覧（詳細設計書 6.2 の手順 3）。パスワードの設定時に、この一覧に含まれるものを拒否する。

- 出典: SecLists `Passwords/Common-Credentials/100k-most-used-passwords-NCSC.txt`
  （<https://github.com/danielmiessler/SecLists>）
- ライセンス: MIT License（Copyright (c) 2018 Daniel Miessler）
- 加工: Unicode の NFKC に正規化して小文字にし、重複を除いて gzip で圧縮した（97,746 件）

更新するときは、同じ手順で作り直す。
