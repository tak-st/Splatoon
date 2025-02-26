身内用です。

あまりテスト出来ていないコードが含まれます。

自己責任で使用してください。

自動アップデートは行われませんので、定期的にインポートし直してください。

## P1 シンソイルセイバー
公式スクリプトのv2に追従済
- 表示形式を数値+Textに変更
- 1,2本目の処理用の位置がマーカーで示されるように変更（西東分かれの方式のみ）
- ↑ 3,4本目になってもそのままの表示の為注意
```
https://github.com/tak-st/Splatoon/raw/main/SplatoonScripts/Duties/Dawntrail/The%20Futures%20Rewritten/P1%20Fall%20of%20Faith.cs
```

## P2 光の暴走JP
公式スクリプトのv2に追従済
- AoEが付いた際に担当の塔の表示をしない機能を追加
```
https://github.com/tak-st/Splatoon/raw/main/SplatoonScripts/Duties/Dawntrail/The%20Futures%20Rewritten/P2%20Light%20Rampant%20JP.cs
```

## P2 自動ターゲットクリスタル
公式スクリプトのv1に追従済
- インターバル変更機能を追加
- 一定範囲以内にある氷晶だけを対象にする を追加
- HPが低い氷晶は対象としない を追加
```
https://github.com/tak-st/Splatoon/raw/main/SplatoonScripts/Duties/Dawntrail/The%20Futures%20Rewritten/P2%20AutoTargetCrystal.cs
```

## P4 自動ターゲットスイッチ
公式スクリプトのv8に追従済
- 設定の日本語化
- 許容範囲内時にターゲットをランダムに選択しない を追加
```
https://github.com/tak-st/Splatoon/raw/main/SplatoonScripts/Duties/Dawntrail/The%20Futures%20Rewritten/P4%20AutoTargetSwitcher.cs
```

## P4 時間結晶
公式スクリプトのv12に追従済
- 設定の日本語化
- 各フェーズの残り時間を表示
- ハムカツ式のタンク位置に対応
- 自身から出現位置が遠い竜に当たる を追加
- 赤エアロ時、波の避け方を表示
- ランダム待機範囲 (秒) の上限を14秒まで増加
- リターン位置が北かつ担当位置が2または3の場合、テイカー散会時に白床を回収 を追加
- 各行動の制限時間が表示されるように変更
- リターン位置の表示位置を調整
- リターン位置、白床位置にテキスト表示を追加
- 注意テキストの表示内容を増加
- 位置を強制表示する残り時間 を追加
- 他人を表示するオプションで動作とリターン位置を別々に設定できるように
- 担当でない白床も表示出来るように
- 青デバフ時に何らかの原因でマーカーが付かなかった場合にコマンドを再実行する機能を追加
```
https://github.com/tak-st/Splatoon/raw/main/SplatoonScripts/Duties/Dawntrail/The%20Futures%20Rewritten/P4%20Crystallize%20Time.cs
```
### ハムカツ式の場合
```
竜当たりタイミング: Late
自身から出現位置が遠い竜に当たる: オン
赤ブリで竜に当たった際に北に行くべきか: オフ
マーカーによる優先度: オン
青デバフ時に実行するコマンド: /mk attack <me>
リターン位置が北かつ担当位置が2または3の場合、テイカー散会時に白床を回収: オン
ハムカツのタンクリターン位置: オン
波(タンクでなく、MT組の場合): 上から西東東西
波(タンクでなく、ST組の場合): 上から東西西東
```

## P5 パラダイスリゲインド
公式スクリプトのv4に追従済
- 2個目の安全塔に入る設定の際に1個目の安全塔が光るように（ぬけまる式で使用）
- 中央に安置の方向・近づく離れるが表示されるように
- 攻撃方向が確定してから表示を行うように
```
https://github.com/tak-st/Splatoon/raw/main/SplatoonScripts/Duties/Dawntrail/The%20Futures%20Rewritten/P5%20Paradise%20Regained.cs
```