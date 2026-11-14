# Unity チート体験・対策教材

Unity のローカルなシューティング環境で、ESP・壁越し表示などの仕組みと対策を学ぶ日本語教材です。**このリポジトリは Unity プロジェクト単体ではありません。** Unity 公式テンプレートを各自で取得してから、教材を追加します。公開サーバーや他人のゲームには使用しないでください。

## 事前準備

- Unity Hub と、利用条件を満たした Unity アカウント・ライセンス。
- Unity Editor **6000.3.27f2**。この教材の検証対象です。
- Unity Hub のテンプレート **Multiplayer Third Person Gameplay 1.0.8**。この版以外では導入処理やコードが合わない場合があります。
- Python **3.10 以降**（Windows ではインストール時に `py` または `python` コマンドを使えるようにする）。
- Unity Hub や Package Manager がテンプレートとパッケージを取得するためのインターネット接続。初回は時間とディスク容量が必要です。Windows ではパスが長すぎると素材の読み込みエラーが起きる場合があるため、`D:\UnityProjects\CheatStudy` のような短い場所を推奨します。

Unity のセキュリティ更新は [Brotli の勧告](https://unity.com/security/unity-brotli-decoder-vulnerability)と[証明書に関する勧告](https://unity.com/security/unity-emudhra-compromise)を参照してください。配布済み Player は修正版 Editor で再ビルドが必要です。

## 1. 公式テンプレートを作る

1. Unity Hub で Editor **6000.3.27f2** をインストールします。
2. 「新しいプロジェクト」で同 Editor を選び、**Multiplayer Third Person Gameplay** テンプレート（1.0.8）を選んで、新しい空きフォルダーにプロジェクトを作ります。テンプレートは Unity Hub から各自で取得してください。
3. 初回のインポートが始まった場合は完了を待ち、**Unity Editor を終了**します。ほかの大切なプロジェクトには適用しないでください。
4. このリポジトリを Git で取得するか、GitHub の「Code > Download ZIP」で保存・展開します。

## 2. 教材を導入する

Windows の PowerShell で、この README のある教材フォルダーに移動し、以下を実行します。`D:\UnityProjects\CheatStudy` は手順 1 で作った **Unity プロジェクトのフォルダー**に置き換えます。

```powershell
py .\tools\install.py 'D:\UnityProjects\CheatStudy'
```

`py` が使えない環境では `python` に置き換えてください。macOS / Linux では `python3 tools/install.py '/作成した/プロジェクト'` を使用します。このコマンドは教材の `Assets/CheatEducation/` をコピーし、Package Manager の依存バージョンを設定して、テンプレートの `GameNetworkManager` に初期化・終了処理の修正を適用します。変更前の 2 ファイルはプロジェクト内の `.cheat-education-backup/` に保存します。

既に教材を導入したプロジェクト、既存のバックアップがあるプロジェクト、対象ファイルが想定と異なるプロジェクトには上書きせず停止します。やり直す場合は別の新規テンプレートを作成してください。

**Netcode for GameObjects 2.14.0 は Unity 6.3 ではプレリリースです。** 開発用教材として使用し、製品版に適用する場合は別途互換性を検証してください。導入時に他の関連パッケージも検証時のバージョンへ変更します。

## 3. Unity で動かす

1. Unity Hub で手順 1 のプロジェクトを開きます。パッケージ解決・コンパイルを待ちます。
2. Project ウィンドウで `Assets/Shooter/TestScenes/[BB] Shooter.unity` を開きます。`MultiplayerSession` が名前に付く方ではありません。
3. メニュー **Tools > Cheat Education > レッスン01 をセットアップ** を選び、シーンを保存します。この操作で練習用ボット・パネルを配置します。
4. Play を押して **Start Host** を選びます。Game 画面をクリックし、F1 でパネル表示、F2 で ESP、F3 で壁越しシルエットを切り替えます。
5. 終了時は Play を止めます。ネットワークの実験は localhost / Multiplayer Play Mode のみで行います。

操作が反応しない場合は Game 画面のフォーカスを確認し、ノート PC では Fn キーも確認してください。ボットが見えない場合は周囲を見回し、Console のエラーを確認してください。

## 学習内容と制約

- [レッスン 00〜05](LESSONS/) と [チートと対策の早見表](DEFENSE_AND_THEORY.md)。レッスン内のセットアップ記述には旧手順が含まれるため、事前準備と導入はこの README を優先してください。
- ESP・ウォールハック、エイム・武器・移動に関する教材コードを収録。命中・移動の対策には未接続の雛形があります。
- `ServerVisibilityFilter` は可視性対策の**試作**です。フィルタを追加し、壁レイヤーを設定しないと有効になりません。別プロセスのローカルクライアントで 1 回、壁による非表示と壁撤去後の再表示を確認しています。接続時からの遮蔽、同期中の漏洩、再接続、複数クライアント、別 PC は未検証です。完成した防御として扱わないでください。
- 通常 Player ビルドで X-Ray シェーダーが同梱されるかは未検証です。

## 今回の検証（2026-11-14）

Unity Hub に保存されていた 1.0.8 の公式テンプレートから検証用プロジェクトを作り、Windows / Unity 6000.3.27f2 で導入・C# コンパイル・ローカル Shooter シーンの Play・Start Host・ボット 3 体の生成を確認しました。パッケージの解決には、別の検証環境から複製した PackageCache を使用しています。Unity Hub の画面操作による新規作成と、キャッシュのない環境での取得は今回検証していません。

検証環境の Editor では `com.unity.render-pipelines.core` の `TraceRenderingLayerMask.urtshader` に関する `Host type is not matching any asset type` エラーが表示されました。今回のローカルホストとボット生成は行えましたが、原因と描画への影響は未解明です。長いプロジェクトパスでは別途 VFX 読込エラーが起き、短いパスの検証コピーで該当 VFX を再インポートできました。F3 の再検証、全レッスン、別 PC は今回確認していません。

## 配布物

このリポジトリに Unity のテンプレート本体、テンプレート由来の画像・フォント、Unity Editor、PackageCache は含めません。テンプレートとパッケージは利用者が Unity から各自取得し、それぞれの利用条件に従ってください。教材独自部分の利用ライセンスは未指定です。
