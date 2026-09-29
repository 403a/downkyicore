# Design Documents

此目錄只保存仍需理解原因與責任歸屬的設計決策。Current topology 與 invariant
以根層 `ARCHITECTURE.md` 為準；目前工作以 GitHub Issue #137 與其連結為準；完成證據
由 Git、PR 與已關閉 Issue 保存。

- `aria2-rpc-client-ownership.md`：aria2 RPC compatibility adapter 的責任分割。
- `list-search-navigation.md`：數字 list URL、投稿／收藏搜尋與返回狀態保留的 typed-navigation 決策。
- `desktop-feature-locality.md`：Desktop routed-feature identity、Shell metadata locality、route completeness 與拒絕全域 FeatureRegistry 的設計決策。
- `logging-ownership-sink-adr.md`：logging privacy boundary、Infrastructure owner、rolling sink、retention 與 diagnostic export 決策。
- `typed-navigation-user-space-compatibility.md`：typed route、history 與 UserSpace 相容契約。
- 根層 `ARCHITECTURE.md`：current owner、依賴方向、invariant 與可執行防線的權威入口。

設計文件不保存 current work、舊 run、Gate、SHA 或可由程式產生的 inventory。
