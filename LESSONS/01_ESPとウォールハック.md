> **事前準備・導入手順は [README](../README.md) を優先してください。以下には旧環境の記述や未実装の例があります。**

# レッスン01 ESP とウォールハック

**チート**: 敵の位置・体力・向きを画面に常時表示（ESP）。壁の向こうの敵をシルエットで表示（ウォールハック）。
**対策**: サーバーが「そのクライアントから実際に見える敵」だけを送る（`NetworkObject` の可視性制御）。

> ★ `Multiplayer Third Person Gameplay` テンプレート（1.0.8）向けの**具体的な手順・実コード対応**は
> プロジェクト同梱の `Assets/CheatEducation/レッスン01_ウォールハック.md` を参照。
> 本ファイルは仕組みと対策の解説。テンプレートの実クラス名:
> 体力 = `Blocks.Gameplay.Core.CoreStatsHandler.GetCurrentValue(StatKeys.Health)`（`NetworkList` で Read=Everyone）、
> 名前 = `CorePlayerState.PlayerName`、プレイヤー体 = `[BB] ShooterPlayer` 内の `Armature_Mesh`（SkinnedMeshRenderer）、
> ローカルシーン = `Assets/Shooter/TestScenes/[BB] Shooter.unity`（127.0.0.1 構成・オンライン不要。
> `MultiplayerSession` 付きは使わない）。練習相手はボット（`Tools > Cheat Education > レッスン01 をセットアップ`）。

---

## 1. チートの説明

- **ESP (Extra Sensory Perception)**: 敵の位置に四角い枠、頭上に体力バー、画面端に方向表示。
  壁も床も無視して「今どこに誰がいるか」が全部分かる。FPS で最も蔓延しているチート類型
  （ある調査ではチート検出の 58% がウォールハック系）。
- **ウォールハック**: 敵モデルそのものを壁越しに描画する。深度テスト（手前の壁で隠す処理）を
  無効にした素通しシルエットを重ねる。

どちらも共通点は **「敵の座標さえ分かれば成立する」**。エイムを動かすわけでも
ダメージを変えるわけでもない。**情報を見るだけ**のチート。

---

## 2. なぜ効くのか（Netcode for GameObjects の仕組みから）

NGO の既定動作:

1. `NetworkObject` を持つオブジェクト（＝各プレイヤー）は、**スポーン時に全クライアントへ複製**される。
2. `NetworkTransform` が、その位置・回転を**毎ネットワークtick、全クライアントへ同期**する。
3. この同期に「壁があるか」「見えているか」という判定は**入っていない**。
   NGO は「近くにいるか」すら見ていない（既定では距離カリングもオフ）。

つまり **あなたのゲームクライアントのメモリには、マップ上の全プレイヤーの正確な座標が、
毎フレーム更新されて存在している**。壁の裏だろうと関係ない。
チートは Unity の API（`FindObjectsByType<NetworkObject>()` や `NetworkManager.SpawnManager`）で
その一覧を取り、画面に描くだけでよい。

> これは「NGO が雑」なのではなく、**多くのゲームがこの既定のまま出荷している**のが問題。
> 対策（後述）は開発者が明示的に足す必要がある。

---

## 3. テンプレートのどこを見るか（調査手順）

```bash
# プレイヤープレハブと NetworkTransform
grep -rn "NetworkTransform\|NetworkObject" Assets/Shooter Assets/Core --include=*.cs
```

- `Assets/Shooter/` のプレイヤープレハブ（`Shooter Player` 等）を選び、Inspector で
  `NetworkObject` と `NetworkTransform` があることを確認。
- `CoreStatsHandler`（体力）を開き、体力が `NetworkVariable<int/float>` かどうか確認
  （`NetworkVariable` なら**その値もクライアントに同期されている**＝ESP に体力を出せる）。
- シーンの `NetworkManager` で「Distance-based culling」や独自の可視性処理が**無い**ことを確認。

---

## 4. チートの実装

### 使うスクリプト
- `Scripts/NetPlayerRegistry.cs` … 自分以外のプレイヤー `NetworkObject` を毎フレーム集める
- `Scripts/EspOverlayCheat.cs` … 2D の枠・距離・体力を GUI 描画
- `Scripts/WallhackCheat.cs` ＋ `Shaders/XRaySilhouette.shader` … 壁越しシルエット
- `Scripts/CheatConsole.cs` … F1 で開くトグルパネル（上記をまとめて ON/OFF）

### 手順
1. Shooter シーンに空の GameObject 「CheatConsole」を作り、`CheatConsole` を付ける
   （`autoAddCheats = true` なら他のチートも自動で乗る）。
2. `NetPlayerRegistry` も同じ GameObject に付く（CheatConsole が自動追加）。
3. Play（Multiplayer Play Mode で Host + Client）。**F1** でパネルを開き
   「ESP」「ウォールハック」を ON。
4. 壁の裏に回った敵が、枠とシルエットで見え続けることを確認。

### コードのポイント（`NetPlayerRegistry.cs`）
```csharp
// NGO が管理する「スポーン済み NetworkObject」を全部見られる。
foreach (var kv in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
{
    var no = kv.Value;
    if (no == NetworkManager.Singleton.LocalClient?.PlayerObject) continue; // 自分は除外
    if (!IsPlayer(no)) continue;                                            // プレイヤーだけ
    _players.Add(no);   // ← この座標 no.transform.position が「壁の裏でも正確」
}
```
`SpawnManager.SpawnedObjects` に**見えないはずの敵も入っている**——これがチートの根拠。

---

## 5. 対策の実装（サーバー側可視性フィルタ）

### 考え方
「クライアントが持っている座標を隠す」のは無理（描画に必要な座標は結局送る）。
正しい対策は **「そのクライアントから見えない敵の座標は、そもそも送らない」**。
＝ サーバーだけが全員の位置を知り、各クライアントには「見えるぶんだけ」複製する。

これは新発明ではなく **業界標準**:
- Quake3 系（ioquake3）: サーバーが PVS（Potentially Visible Set）＋視線判定で送信を絞る
- BlackMirror（ACM CCS 2020）: 信頼実行環境で可視性テスト → 見えるものだけ選択的開示
- 米国特許 US 11,771,997 B2: サーバー GPU がレイトレで可視性行列を作りパケットを絞る

NGO はこれを実装するための API を持っている:

| API | 役割 |
|---|---|
| `NetworkObject.CheckObjectVisibility = (clientId) => bool` | **スポーン時／新規接続時**に「この client にこのオブジェクトを複製するか」を決める delegate |
| `NetworkObject.NetworkShow(clientId)` | 後から可視にする（**サーバーのみ**） |
| `NetworkObject.NetworkHide(clientId)` | 後から不可視にする（**サーバーのみ**。クライアント側ではオブジェクトが Despawn する） |

> ★ 既知の注意（NGO の Issue でも議論されている）: `CheckObjectVisibility` は**スポーン時にしか
> 呼ばれない**。プレイ中に「見えた／隠れた」が変わるものは、**毎tick 自分で `NetworkShow/NetworkHide`
> を呼ぶ**必要がある。下の `ServerVisibilityFilter` はそれをやる。

### 使うスクリプト
`Countermeasures/ServerVisibilityFilter.cs`

- プレイヤープレハブに付ける（各プレイヤーに 1 つ）。
- **サーバーでのみ**動作し、毎tick（間引き可）以下を行う:
  1. 全「観測者クライアント」について、
  2. この敵が観測者から見えるか判定（距離 + `Physics.Linecast` で壁チェック）
  3. 見える → `NetworkShow(observerId)` / 見えない → `NetworkHide(observerId)`
- 「見えなくなった直後の猶予（grace period）」を持たせ、チラつきと
  “隠れた瞬間に消える”不自然さを緩和。

### 接続手順
1. `Assets/Shooter/` のプレイヤープレハブに `ServerVisibilityFilter` を追加。
2. `wallMask` に壁・地形のレイヤーを設定（プレイヤー・弾は除外）。
3. `eyeHeight` を頭の高さ、`maxDistance` をマップ規模に合わせる。
4. Play。**ESP チートを ON にしたまま**、敵が壁の裏に入ると
   ESP の枠も消える（＝クライアントにもう座標が来ていない）ことを確認。

---

## 6. 体験・観察手順

| 手順 | 期待される結果 | 意味 |
|---|---|---|
| 対策なし・ESP ON | 壁の裏の敵も枠が出る | クライアントに全座標がある |
| 対策なし・ウォールハック ON | 壁の裏の敵のシルエットが見える | 同上（描画に使える） |
| `ServerVisibilityFilter` を付ける・ESP ON | 敵が物陰に入ると枠が消える | サーバーが送るのをやめた＝**クライアントに座標が無い** |
| 敵が物陰から出る | 数フレーム後に枠が復活 | サーバーが `NetworkShow` した |

**F1 パネルの「ESP」ステータス行**に「見えている敵 N 体」が出る。対策 ON で N が
物陰の敵ぶん減れば成功。

---

## 7. 対策の限界（ここが本題）

| 限界 | 内容 |
|---|---|
| **計算コスト** | 観測者 × 対象 の総当たりで毎tick Linecast。10 人なら 90 回/tick。人数と tick rate に比例して増える。実運用は「候補を PVS や距離で粗く絞ってから視線判定」の二段構え（＝ioquake3 と同じ） |
| **peeker's advantage** | 「見えた瞬間に送る」ので、角から出る側は相手より数十 ms 早く相手を認識できる。完全には消せない |
| **音・気配の情報** | `NetworkHide` はオブジェクトごと消すので、足音や銃声を「位置を出さずに」聞かせる、といった部分開示が難しい。BlackMirror はこの粒度を扱うために専用設計している |
| **完全な遮蔽判定の難しさ** | 1 本の Linecast は「頭の 1 点」しか見ていない。体の一部だけ見えるケースを正しく扱うにはサンプル数を増やす＝さらにコスト増 |
| **クライアントを信じる部分が残る** | 「観測者の視点位置」はサーバーが `NetworkTransform`（Server Authority なら安全／Owner Authority なら偽装可能）から取る。04 レッスンに続く |

### まとめ
ESP/ウォールハックの**クライアント側での検知**（不審コンポーネントの走査、描画フックの検出）は
どれも回避可能で「時間稼ぎ」にしかならない。
**唯一の構造的対策は「見えないものを送らない」**。NGO はそのための API を持っているが、
**既定では有効になっておらず、正しく（毎tick・視線判定つきで）実装するのは開発者の責任**。

→ [レッスン02 エイムボットとホーミング](02_エイムボットとホーミング.md) へ。
