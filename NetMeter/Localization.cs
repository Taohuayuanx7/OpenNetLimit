using System.Globalization;
using System.IO;
using System.Text.Json;

namespace NetMeter;

/// <summary>
/// String-based localization. zh-CN is the built-in default; en-US, ko-KR,
/// ja-JP, es-ES, pt-BR, fr-FR and de-DE are provided as alternatives. The
/// chosen language is persisted to settings.json and restored on startup.
/// </summary>
public static class L
{
    public const string ZhCn = "zh-CN";
    public const string ZhTw = "zh-TW";
    public const string EnUs = "en-US";
    public const string KoKr = "ko-KR";
    public const string JaJp = "ja-JP";
    public const string EsEs = "es-ES";
    public const string PtBr = "pt-BR";
    public const string FrFr = "fr-FR";
    public const string DeDe = "de-DE";

    public static readonly string[] All =
    {
        ZhCn, ZhTw, EnUs, KoKr, JaJp, EsEs, PtBr, FrFr, DeDe
    };

    private static readonly Dictionary<string, string> Zh = new()
    {
        ["app.Title"] = "NetMeter - 进程网络监控与限速",

        ["col.Pid"] = "PID",
        ["col.Process"] = "进程",
        ["col.Tcp"] = "TCP",
        ["col.Udp"] = "UDP",
        ["col.Down"] = "↓ KB/s",
        ["col.Up"] = "↑ KB/s",
        ["col.DownLimit"] = "下行限",
        ["col.UpLimit"] = "上行限",
        ["col.TcpLimit"] = "TCP上限",
        ["col.UdpLimit"] = "UDP上限",

        ["main.NotStarted"] = "未启动",
        ["main.Running"] = "运行中",
        ["main.Stopped"] = "已停止",
        ["main.StartFailed"] = "启动失败",
        ["main.ToggleStop"] = "停止监控",
        ["main.ToggleStart"] = "开始监控",
        ["main.LimitedPrograms"] = "已限制程序",
        ["main.Filter"] = "筛选:",
        ["main.Refresh"] = "刷新:",
        ["main.Language"] = "语言:",
        ["main.Blocked"] = " | 拦截:",
        ["main.Dropped"] = " | 丢弃:",
        ["main.AppliedLimit"] = "已应用限制：{0}",
        ["main.StartError"] = "启动 WinDivert 失败：{0}\n\n请以管理员身份运行本程序。",
        ["main.WinDivertFunc"] = "（WinDivert 函数: {0}）",

        ["lp.Title"] = "已限制程序",
        ["lp.Hint"] = "双击行或选中后点击「调整」修改限制，0 = 不限",
        ["lp.Status"] = "状态",
        ["lp.DownKb"] = "下行 KB/s",
        ["lp.UpKb"] = "上行 KB/s",
        ["lp.Tcp"] = "TCP上限",
        ["lp.Udp"] = "UDP上限",
        ["lp.Adjust"] = "调整",
        ["lp.Delete"] = "删除",
        ["lp.Close"] = "关闭",
        ["lp.Running"] = "运行中",
        ["lp.NotRunning"] = "未运行",

        ["ld.Title"] = "设置限制",
        ["ld.TitleWithName"] = "设置限制 - {0}",
        ["ld.Down"] = "下行 KB/s:",
        ["ld.Up"] = "上行 KB/s:",
        ["ld.Tcp"] = "TCP上限:",
        ["ld.Udp"] = "UDP上限:",
        ["ld.ZeroHint"] = "0 = 不限",
        ["ld.Clear"] = "清除限制",
        ["ld.Cancel"] = "取消",
        ["ld.Apply"] = "应用",
        ["ld.CurrentRateNone"] = "当前速率: -",
        ["ld.CurrentRate"] = "当前速率: ↓ {0} KB/s   ↑ {1} KB/s",
        ["err.Down"] = "下行速率必须是非负整数（KB/s）。",
        ["err.Up"] = "上行速率必须是非负整数（KB/s）。",
        ["err.Tcp"] = "TCP 上限必须是非负整数。",
        ["err.Udp"] = "UDP 上限必须是非负整数。"
    };

    private static readonly Dictionary<string, string> ZhTrad = new()
    {
        ["app.Title"] = "NetMeter - 處理程序網路監控與限速",

        ["col.Pid"] = "PID",
        ["col.Process"] = "處理程序",
        ["col.Tcp"] = "TCP",
        ["col.Udp"] = "UDP",
        ["col.Down"] = "↓ KB/s",
        ["col.Up"] = "↑ KB/s",
        ["col.DownLimit"] = "下載限制",
        ["col.UpLimit"] = "上傳限制",
        ["col.TcpLimit"] = "TCP上限",
        ["col.UdpLimit"] = "UDP上限",

        ["main.NotStarted"] = "未啟動",
        ["main.Running"] = "執行中",
        ["main.Stopped"] = "已停止",
        ["main.StartFailed"] = "啟動失敗",
        ["main.ToggleStop"] = "停止監控",
        ["main.ToggleStart"] = "開始監控",
        ["main.LimitedPrograms"] = "已限制程式",
        ["main.Filter"] = "篩選:",
        ["main.Refresh"] = "重新整理:",
        ["main.Language"] = "語言:",
        ["main.Blocked"] = " | 封鎖:",
        ["main.Dropped"] = " | 丟棄:",
        ["main.AppliedLimit"] = "已套用限制：{0}",
        ["main.StartError"] = "啟動 WinDivert 失敗：{0}\n\n請以系統管理員身分執行本程式。",
        ["main.WinDivertFunc"] = "（WinDivert 函式: {0}）",

        ["lp.Title"] = "已限制程式",
        ["lp.Hint"] = "雙擊列或選取後點擊「調整」修改限制，0 = 不限",
        ["lp.Status"] = "狀態",
        ["lp.DownKb"] = "下載 KB/s",
        ["lp.UpKb"] = "上傳 KB/s",
        ["lp.Tcp"] = "TCP上限",
        ["lp.Udp"] = "UDP上限",
        ["lp.Adjust"] = "調整",
        ["lp.Delete"] = "刪除",
        ["lp.Close"] = "關閉",
        ["lp.Running"] = "執行中",
        ["lp.NotRunning"] = "未執行",

        ["ld.Title"] = "設定限制",
        ["ld.TitleWithName"] = "設定限制 - {0}",
        ["ld.Down"] = "下載 KB/s:",
        ["ld.Up"] = "上傳 KB/s:",
        ["ld.Tcp"] = "TCP上限:",
        ["ld.Udp"] = "UDP上限:",
        ["ld.ZeroHint"] = "0 = 不限",
        ["ld.Clear"] = "清除限制",
        ["ld.Cancel"] = "取消",
        ["ld.Apply"] = "套用",
        ["ld.CurrentRateNone"] = "目前速率: -",
        ["ld.CurrentRate"] = "目前速率: ↓ {0} KB/s   ↑ {1} KB/s",
        ["err.Down"] = "下載速率必須是非負整數（KB/s）。",
        ["err.Up"] = "上傳速率必須是非負整數（KB/s）。",
        ["err.Tcp"] = "TCP 上限必須是非負整數。",
        ["err.Udp"] = "UDP 上限必須是非負整數。"
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["app.Title"] = "NetMeter - Process Monitor & Limiter",

        ["col.Pid"] = "PID",
        ["col.Process"] = "Process",
        ["col.Tcp"] = "TCP",
        ["col.Udp"] = "UDP",
        ["col.Down"] = "↓ KB/s",
        ["col.Up"] = "↑ KB/s",
        ["col.DownLimit"] = "Down Limit",
        ["col.UpLimit"] = "Up Limit",
        ["col.TcpLimit"] = "TCP Max",
        ["col.UdpLimit"] = "UDP Max",

        ["main.NotStarted"] = "Not Started",
        ["main.Running"] = "Running",
        ["main.Stopped"] = "Stopped",
        ["main.StartFailed"] = "Start Failed",
        ["main.ToggleStop"] = "Stop Monitoring",
        ["main.ToggleStart"] = "Start Monitoring",
        ["main.LimitedPrograms"] = "Limited Programs",
        ["main.Filter"] = "Filter:",
        ["main.Refresh"] = "Refresh:",
        ["main.Language"] = "Language:",
        ["main.Blocked"] = " | Blocked:",
        ["main.Dropped"] = " | Dropped:",
        ["main.AppliedLimit"] = "Applied limits: {0}",
        ["main.StartError"] = "Failed to start WinDivert: {0}\n\nPlease run this program as administrator.",
        ["main.WinDivertFunc"] = " (WinDivert function: {0})",

        ["lp.Title"] = "Limited Programs",
        ["lp.Hint"] = "Double-click a row or select it and click \"Adjust\" to modify limits; 0 = unlimited",
        ["lp.Status"] = "Status",
        ["lp.DownKb"] = "Down KB/s",
        ["lp.UpKb"] = "Up KB/s",
        ["lp.Tcp"] = "TCP Max",
        ["lp.Udp"] = "UDP Max",
        ["lp.Adjust"] = "Adjust",
        ["lp.Delete"] = "Delete",
        ["lp.Close"] = "Close",
        ["lp.Running"] = "Running",
        ["lp.NotRunning"] = "Not Running",

        ["ld.Title"] = "Set Limits",
        ["ld.TitleWithName"] = "Set Limits - {0}",
        ["ld.Down"] = "Down KB/s:",
        ["ld.Up"] = "Up KB/s:",
        ["ld.Tcp"] = "TCP Max:",
        ["ld.Udp"] = "UDP Max:",
        ["ld.ZeroHint"] = "0 = unlimited",
        ["ld.Clear"] = "Clear Limits",
        ["ld.Cancel"] = "Cancel",
        ["ld.Apply"] = "Apply",
        ["ld.CurrentRateNone"] = "Current rate: -",
        ["ld.CurrentRate"] = "Current rate: ↓ {0} KB/s   ↑ {1} KB/s",
        ["err.Down"] = "Down rate must be a non-negative integer (KB/s).",
        ["err.Up"] = "Up rate must be a non-negative integer (KB/s).",
        ["err.Tcp"] = "TCP max must be a non-negative integer.",
        ["err.Udp"] = "UDP max must be a non-negative integer."
    };

    private static readonly Dictionary<string, string> Ko = new()
    {
        ["app.Title"] = "NetMeter - 프로세스 네트워크 모니터링 및 속도 제한",

        ["col.Pid"] = "PID",
        ["col.Process"] = "프로세스",
        ["col.Tcp"] = "TCP",
        ["col.Udp"] = "UDP",
        ["col.Down"] = "↓ KB/s",
        ["col.Up"] = "↑ KB/s",
        ["col.DownLimit"] = "다운로드 제한",
        ["col.UpLimit"] = "업로드 제한",
        ["col.TcpLimit"] = "TCP 상한",
        ["col.UdpLimit"] = "UDP 상한",

        ["main.NotStarted"] = "미시작",
        ["main.Running"] = "실행 중",
        ["main.Stopped"] = "중지됨",
        ["main.StartFailed"] = "시작 실패",
        ["main.ToggleStop"] = "모니터링 중지",
        ["main.ToggleStart"] = "모니터링 시작",
        ["main.LimitedPrograms"] = "제한된 프로그램",
        ["main.Filter"] = "필터:",
        ["main.Refresh"] = "새로고침:",
        ["main.Language"] = "언어:",
        ["main.Blocked"] = " | 차단:",
        ["main.Dropped"] = " | 폐기:",
        ["main.AppliedLimit"] = "{0} 제한 적용됨",
        ["main.StartError"] = "WinDivert 시작 실패: {0}\n\n관리자 권한으로 실행하십시오.",
        ["main.WinDivertFunc"] = " (WinDivert 함수: {0})",

        ["lp.Title"] = "제한된 프로그램",
        ["lp.Hint"] = "행을 두 번 클릭하거나 선택 후 「조정」을 클릭하여 제한을 수정하세요. 0 = 제한 없음",
        ["lp.Status"] = "상태",
        ["lp.DownKb"] = "다운로드 KB/s",
        ["lp.UpKb"] = "업로드 KB/s",
        ["lp.Tcp"] = "TCP 상한",
        ["lp.Udp"] = "UDP 상한",
        ["lp.Adjust"] = "조정",
        ["lp.Delete"] = "삭제",
        ["lp.Close"] = "닫기",
        ["lp.Running"] = "실행 중",
        ["lp.NotRunning"] = "미실행",

        ["ld.Title"] = "제한 설정",
        ["ld.TitleWithName"] = "{0} 제한 설정",
        ["ld.Down"] = "다운로드 KB/s:",
        ["ld.Up"] = "업로드 KB/s:",
        ["ld.Tcp"] = "TCP 상한:",
        ["ld.Udp"] = "UDP 상한:",
        ["ld.ZeroHint"] = "0 = 제한 없음",
        ["ld.Clear"] = "제한 해제",
        ["ld.Cancel"] = "취소",
        ["ld.Apply"] = "적용",
        ["ld.CurrentRateNone"] = "현재 속도: -",
        ["ld.CurrentRate"] = "현재 속도: ↓ {0} KB/s   ↑ {1} KB/s",
        ["err.Down"] = "다운로드 속도는 음수가 아닌 정수여야 합니다 (KB/s).",
        ["err.Up"] = "업로드 속도는 음수가 아닌 정수여야 합니다 (KB/s).",
        ["err.Tcp"] = "TCP 상한은 음수가 아닌 정수여야 합니다.",
        ["err.Udp"] = "UDP 상한은 음수가 아닌 정수여야 합니다."
    };

    private static readonly Dictionary<string, string> Ja = new()
    {
        ["app.Title"] = "NetMeter - プロセスネットワーク監視・速度制限",

        ["col.Pid"] = "PID",
        ["col.Process"] = "プロセス",
        ["col.Tcp"] = "TCP",
        ["col.Udp"] = "UDP",
        ["col.Down"] = "↓ KB/s",
        ["col.Up"] = "↑ KB/s",
        ["col.DownLimit"] = "下り制限",
        ["col.UpLimit"] = "上り制限",
        ["col.TcpLimit"] = "TCP上限",
        ["col.UdpLimit"] = "UDP上限",

        ["main.NotStarted"] = "未起動",
        ["main.Running"] = "実行中",
        ["main.Stopped"] = "停止中",
        ["main.StartFailed"] = "起動失敗",
        ["main.ToggleStop"] = "監視停止",
        ["main.ToggleStart"] = "監視開始",
        ["main.LimitedPrograms"] = "制限済みプログラム",
        ["main.Filter"] = "フィルター:",
        ["main.Refresh"] = "更新:",
        ["main.Language"] = "言語:",
        ["main.Blocked"] = " | ブロック:",
        ["main.Dropped"] = " | 破棄:",
        ["main.AppliedLimit"] = "{0} に制限を適用しました",
        ["main.StartError"] = "WinDivert の起動に失敗しました: {0}\n\n管理者として実行してください。",
        ["main.WinDivertFunc"] = " (WinDivert 関数: {0})",

        ["lp.Title"] = "制限済みプログラム",
        ["lp.Hint"] = "行をダブルクリック、または選択して「調整」をクリックすると制限を変更できます。0 = 無制限",
        ["lp.Status"] = "状態",
        ["lp.DownKb"] = "下り KB/s",
        ["lp.UpKb"] = "上り KB/s",
        ["lp.Tcp"] = "TCP上限",
        ["lp.Udp"] = "UDP上限",
        ["lp.Adjust"] = "調整",
        ["lp.Delete"] = "削除",
        ["lp.Close"] = "閉じる",
        ["lp.Running"] = "実行中",
        ["lp.NotRunning"] = "未実行",

        ["ld.Title"] = "制限の設定",
        ["ld.TitleWithName"] = "{0} の制限設定",
        ["ld.Down"] = "下り KB/s:",
        ["ld.Up"] = "上り KB/s:",
        ["ld.Tcp"] = "TCP上限:",
        ["ld.Udp"] = "UDP上限:",
        ["ld.ZeroHint"] = "0 = 無制限",
        ["ld.Clear"] = "制限を解除",
        ["ld.Cancel"] = "キャンセル",
        ["ld.Apply"] = "適用",
        ["ld.CurrentRateNone"] = "現在の速度: -",
        ["ld.CurrentRate"] = "現在の速度: ↓ {0} KB/s   ↑ {1} KB/s",
        ["err.Down"] = "下り速度は0以上の整数で入力してください（KB/s）。",
        ["err.Up"] = "上り速度は0以上の整数で入力してください（KB/s）。",
        ["err.Tcp"] = "TCP上限は0以上の整数で入力してください。",
        ["err.Udp"] = "UDP上限は0以上の整数で入力してください。"
    };

    private static readonly Dictionary<string, string> Es = new()
    {
        ["app.Title"] = "NetMeter - Monitor y limitador de red por proceso",

        ["col.Pid"] = "PID",
        ["col.Process"] = "Proceso",
        ["col.Tcp"] = "TCP",
        ["col.Udp"] = "UDP",
        ["col.Down"] = "↓ KB/s",
        ["col.Up"] = "↑ KB/s",
        ["col.DownLimit"] = "Lím. descarga",
        ["col.UpLimit"] = "Lím. subida",
        ["col.TcpLimit"] = "Máx. TCP",
        ["col.UdpLimit"] = "Máx. UDP",

        ["main.NotStarted"] = "No iniciado",
        ["main.Running"] = "En ejecución",
        ["main.Stopped"] = "Detenido",
        ["main.StartFailed"] = "Error de inicio",
        ["main.ToggleStop"] = "Detener monitoreo",
        ["main.ToggleStart"] = "Iniciar monitoreo",
        ["main.LimitedPrograms"] = "Programas limitados",
        ["main.Filter"] = "Filtro:",
        ["main.Refresh"] = "Actualizar:",
        ["main.Language"] = "Idioma:",
        ["main.Blocked"] = " | Bloqueados:",
        ["main.Dropped"] = " | Descartados:",
        ["main.AppliedLimit"] = "Límite aplicado: {0}",
        ["main.StartError"] = "No se pudo iniciar WinDivert: {0}\n\nEjecute este programa como administrador.",
        ["main.WinDivertFunc"] = " (Función WinDivert: {0})",

        ["lp.Title"] = "Programas limitados",
        ["lp.Hint"] = "Haga doble clic en una fila o selecciónela y pulse «Ajustar» para modificar los límites; 0 = sin límite",
        ["lp.Status"] = "Estado",
        ["lp.DownKb"] = "Descarga KB/s",
        ["lp.UpKb"] = "Subida KB/s",
        ["lp.Tcp"] = "Máx. TCP",
        ["lp.Udp"] = "Máx. UDP",
        ["lp.Adjust"] = "Ajustar",
        ["lp.Delete"] = "Eliminar",
        ["lp.Close"] = "Cerrar",
        ["lp.Running"] = "En ejecución",
        ["lp.NotRunning"] = "No en ejecución",

        ["ld.Title"] = "Establecer límites",
        ["ld.TitleWithName"] = "Establecer límites - {0}",
        ["ld.Down"] = "Descarga KB/s:",
        ["ld.Up"] = "Subida KB/s:",
        ["ld.Tcp"] = "Máx. TCP:",
        ["ld.Udp"] = "Máx. UDP:",
        ["ld.ZeroHint"] = "0 = sin límite",
        ["ld.Clear"] = "Borrar límites",
        ["ld.Cancel"] = "Cancelar",
        ["ld.Apply"] = "Aplicar",
        ["ld.CurrentRateNone"] = "Velocidad actual: -",
        ["ld.CurrentRate"] = "Velocidad actual: ↓ {0} KB/s   ↑ {1} KB/s",
        ["err.Down"] = "La velocidad de descarga debe ser un entero no negativo (KB/s).",
        ["err.Up"] = "La velocidad de subida debe ser un entero no negativo (KB/s).",
        ["err.Tcp"] = "El máximo de TCP debe ser un entero no negativo.",
        ["err.Udp"] = "El máximo de UDP debe ser un entero no negativo."
    };

    private static readonly Dictionary<string, string> Pt = new()
    {
        ["app.Title"] = "NetMeter - Monitor e limitador de rede por processo",

        ["col.Pid"] = "PID",
        ["col.Process"] = "Processo",
        ["col.Tcp"] = "TCP",
        ["col.Udp"] = "UDP",
        ["col.Down"] = "↓ KB/s",
        ["col.Up"] = "↑ KB/s",
        ["col.DownLimit"] = "Lim. download",
        ["col.UpLimit"] = "Lim. upload",
        ["col.TcpLimit"] = "Máx. TCP",
        ["col.UdpLimit"] = "Máx. UDP",

        ["main.NotStarted"] = "Não iniciado",
        ["main.Running"] = "Em execução",
        ["main.Stopped"] = "Parado",
        ["main.StartFailed"] = "Falha ao iniciar",
        ["main.ToggleStop"] = "Parar monitoramento",
        ["main.ToggleStart"] = "Iniciar monitoramento",
        ["main.LimitedPrograms"] = "Programas limitados",
        ["main.Filter"] = "Filtro:",
        ["main.Refresh"] = "Atualizar:",
        ["main.Language"] = "Idioma:",
        ["main.Blocked"] = " | Bloqueados:",
        ["main.Dropped"] = " | Descartados:",
        ["main.AppliedLimit"] = "Limite aplicado: {0}",
        ["main.StartError"] = "Falha ao iniciar o WinDivert: {0}\n\nExecute este programa como administrador.",
        ["main.WinDivertFunc"] = " (Função WinDivert: {0})",

        ["lp.Title"] = "Programas limitados",
        ["lp.Hint"] = "Clique duas vezes em uma linha ou selecione-a e clique em «Ajustar» para modificar os limites; 0 = sem limite",
        ["lp.Status"] = "Status",
        ["lp.DownKb"] = "Download KB/s",
        ["lp.UpKb"] = "Upload KB/s",
        ["lp.Tcp"] = "Máx. TCP",
        ["lp.Udp"] = "Máx. UDP",
        ["lp.Adjust"] = "Ajustar",
        ["lp.Delete"] = "Excluir",
        ["lp.Close"] = "Fechar",
        ["lp.Running"] = "Em execução",
        ["lp.NotRunning"] = "Não em execução",

        ["ld.Title"] = "Definir limites",
        ["ld.TitleWithName"] = "Definir limites - {0}",
        ["ld.Down"] = "Download KB/s:",
        ["ld.Up"] = "Upload KB/s:",
        ["ld.Tcp"] = "Máx. TCP:",
        ["ld.Udp"] = "Máx. UDP:",
        ["ld.ZeroHint"] = "0 = sem limite",
        ["ld.Clear"] = "Limpar limites",
        ["ld.Cancel"] = "Cancelar",
        ["ld.Apply"] = "Aplicar",
        ["ld.CurrentRateNone"] = "Velocidade atual: -",
        ["ld.CurrentRate"] = "Velocidade atual: ↓ {0} KB/s   ↑ {1} KB/s",
        ["err.Down"] = "A velocidade de download deve ser um inteiro não negativo (KB/s).",
        ["err.Up"] = "A velocidade de upload deve ser um inteiro não negativo (KB/s).",
        ["err.Tcp"] = "O máximo de TCP deve ser um inteiro não negativo.",
        ["err.Udp"] = "O máximo de UDP deve ser um inteiro não negativo."
    };

    private static readonly Dictionary<string, string> Fr = new()
    {
        ["app.Title"] = "NetMeter - Surveillance et limitation réseau par processus",

        ["col.Pid"] = "PID",
        ["col.Process"] = "Processus",
        ["col.Tcp"] = "TCP",
        ["col.Udp"] = "UDP",
        ["col.Down"] = "↓ KB/s",
        ["col.Up"] = "↑ KB/s",
        ["col.DownLimit"] = "Lim. descente",
        ["col.UpLimit"] = "Lim. montée",
        ["col.TcpLimit"] = "Max TCP",
        ["col.UdpLimit"] = "Max UDP",

        ["main.NotStarted"] = "Non démarré",
        ["main.Running"] = "En cours",
        ["main.Stopped"] = "Arrêté",
        ["main.StartFailed"] = "Échec du démarrage",
        ["main.ToggleStop"] = "Arrêter la surveillance",
        ["main.ToggleStart"] = "Démarrer la surveillance",
        ["main.LimitedPrograms"] = "Programmes limités",
        ["main.Filter"] = "Filtre :",
        ["main.Refresh"] = "Actualiser :",
        ["main.Language"] = "Langue :",
        ["main.Blocked"] = " | Bloqués :",
        ["main.Dropped"] = " | Rejetés :",
        ["main.AppliedLimit"] = "Limite appliquée : {0}",
        ["main.StartError"] = "Échec du démarrage de WinDivert : {0}\n\nExécutez ce programme en tant qu'administrateur.",
        ["main.WinDivertFunc"] = " (Fonction WinDivert : {0})",

        ["lp.Title"] = "Programmes limités",
        ["lp.Hint"] = "Double-cliquez sur une ligne ou sélectionnez-la puis cliquez sur « Ajuster » pour modifier les limites ; 0 = illimité",
        ["lp.Status"] = "Statut",
        ["lp.DownKb"] = "Descente KB/s",
        ["lp.UpKb"] = "Montée KB/s",
        ["lp.Tcp"] = "Max TCP",
        ["lp.Udp"] = "Max UDP",
        ["lp.Adjust"] = "Ajuster",
        ["lp.Delete"] = "Supprimer",
        ["lp.Close"] = "Fermer",
        ["lp.Running"] = "En cours",
        ["lp.NotRunning"] = "Non en cours",

        ["ld.Title"] = "Définir les limites",
        ["ld.TitleWithName"] = "Définir les limites - {0}",
        ["ld.Down"] = "Descente KB/s :",
        ["ld.Up"] = "Montée KB/s :",
        ["ld.Tcp"] = "Max TCP :",
        ["ld.Udp"] = "Max UDP :",
        ["ld.ZeroHint"] = "0 = illimité",
        ["ld.Clear"] = "Effacer les limites",
        ["ld.Cancel"] = "Annuler",
        ["ld.Apply"] = "Appliquer",
        ["ld.CurrentRateNone"] = "Vitesse actuelle : -",
        ["ld.CurrentRate"] = "Vitesse actuelle : ↓ {0} KB/s   ↑ {1} KB/s",
        ["err.Down"] = "La vitesse de descente doit être un entier non négatif (KB/s).",
        ["err.Up"] = "La vitesse de montée doit être un entier non négatif (KB/s).",
        ["err.Tcp"] = "Le maximum TCP doit être un entier non négatif.",
        ["err.Udp"] = "Le maximum UDP doit être un entier non négatif."
    };

    private static readonly Dictionary<string, string> De = new()
    {
        ["app.Title"] = "NetMeter - Prozess-Netzwerküberwachung und -limitierung",

        ["col.Pid"] = "PID",
        ["col.Process"] = "Prozess",
        ["col.Tcp"] = "TCP",
        ["col.Udp"] = "UDP",
        ["col.Down"] = "↓ KB/s",
        ["col.Up"] = "↑ KB/s",
        ["col.DownLimit"] = "Download-Limit",
        ["col.UpLimit"] = "Upload-Limit",
        ["col.TcpLimit"] = "TCP-Max",
        ["col.UdpLimit"] = "UDP-Max",

        ["main.NotStarted"] = "Nicht gestartet",
        ["main.Running"] = "Läuft",
        ["main.Stopped"] = "Gestoppt",
        ["main.StartFailed"] = "Start fehlgeschlagen",
        ["main.ToggleStop"] = "Überwachung stoppen",
        ["main.ToggleStart"] = "Überwachung starten",
        ["main.LimitedPrograms"] = "Limitierte Programme",
        ["main.Filter"] = "Filter:",
        ["main.Refresh"] = "Aktualisieren:",
        ["main.Language"] = "Sprache:",
        ["main.Blocked"] = " | Blockiert:",
        ["main.Dropped"] = " | Verworfen:",
        ["main.AppliedLimit"] = "Limit angewendet: {0}",
        ["main.StartError"] = "WinDivert konnte nicht gestartet werden: {0}\n\nFühren Sie dieses Programm als Administrator aus.",
        ["main.WinDivertFunc"] = " (WinDivert-Funktion: {0})",

        ["lp.Title"] = "Limitierte Programme",
        ["lp.Hint"] = "Doppelklicken Sie auf eine Zeile oder wählen Sie sie aus und klicken Sie auf «Anpassen», um Limits zu ändern; 0 = unbegrenzt",
        ["lp.Status"] = "Status",
        ["lp.DownKb"] = "Download KB/s",
        ["lp.UpKb"] = "Upload KB/s",
        ["lp.Tcp"] = "TCP-Max",
        ["lp.Udp"] = "UDP-Max",
        ["lp.Adjust"] = "Anpassen",
        ["lp.Delete"] = "Löschen",
        ["lp.Close"] = "Schließen",
        ["lp.Running"] = "Läuft",
        ["lp.NotRunning"] = "Nicht aktiv",

        ["ld.Title"] = "Limits festlegen",
        ["ld.TitleWithName"] = "Limits festlegen - {0}",
        ["ld.Down"] = "Download KB/s:",
        ["ld.Up"] = "Upload KB/s:",
        ["ld.Tcp"] = "TCP-Max:",
        ["ld.Udp"] = "UDP-Max:",
        ["ld.ZeroHint"] = "0 = unbegrenzt",
        ["ld.Clear"] = "Limits löschen",
        ["ld.Cancel"] = "Abbrechen",
        ["ld.Apply"] = "Übernehmen",
        ["ld.CurrentRateNone"] = "Aktuelle Rate: -",
        ["ld.CurrentRate"] = "Aktuelle Rate: ↓ {0} KB/s   ↑ {1} KB/s",
        ["err.Down"] = "Die Download-Rate muss eine nicht negative Ganzzahl sein (KB/s).",
        ["err.Up"] = "Die Upload-Rate muss eine nicht negative Ganzzahl sein (KB/s).",
        ["err.Tcp"] = "Das TCP-Maximum muss eine nicht negative Ganzzahl sein.",
        ["err.Udp"] = "Das UDP-Maximum muss eine nicht negative Ganzzahl sein."
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new()
    {
        [ZhCn] = Zh,
        [ZhTw] = ZhTrad,
        [EnUs] = En,
        [KoKr] = Ko,
        [JaJp] = Ja,
        [EsEs] = Es,
        [PtBr] = Pt,
        [FrFr] = Fr,
        [DeDe] = De
    };

    public static event Action? Changed;

    private static string _lang = ZhCn;
    private static bool _initialized;

    public static string Lang => _lang;

    private static string SettingsPath => StoragePaths.SettingsPath;

    public static string Tr(string key) =>
        Tables[_lang].TryGetValue(key, out var value)
            ? value
            : Zh.TryGetValue(key, out var fallback) ? fallback : key;

    public static string Tr(string key, params object[] args) => string.Format(Tr(key), args);

    /// <summary>
    /// Loads the persisted language (or detects it from the system culture on
    /// first run and saves it immediately), then applies the fallback rules.
    /// </summary>
    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(StoragePaths.ReadIfSafe(SettingsPath));
                    if (doc.RootElement.TryGetProperty("Language", out var el))
                        _lang = el.GetString() ?? ZhCn;
                }
                catch
                {
                    // Corrupted settings file: keep a copy for inspection
                    try
                    {
                        if (File.Exists(SettingsPath))
                            File.Move(SettingsPath, SettingsPath + ".bad", overwrite: true);
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                _lang = DetectFromSystemCulture();
                Save();
            }
        }
        catch
        {
            _lang = ZhCn;
        }

        if (!Tables.ContainsKey(_lang)) _lang = ZhCn;
    }

    private static string DetectFromSystemCulture()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            // zh-TW / zh-HK / zh-MO use traditional characters; the rest use
            // simplified characters
            return name.Contains("TW") || name.Contains("HK") || name.Contains("MO")
                ? ZhTw
                : ZhCn;
        }
        if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return KoKr;
        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return JaJp;
        if (name.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return EsEs;
        if (name.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return PtBr;
        if (name.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return FrFr;
        if (name.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return DeDe;
        return EnUs;
    }

    public static void Set(string code)
    {
        if (!Tables.ContainsKey(code)) code = ZhCn;
        if (_lang == code) return;
        _lang = code;
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        try
        {
            StoragePaths.AtomicWrite(SettingsPath, JsonSerializer.Serialize(new { Language = _lang }));
        }
        catch
        {
            // Persistence is best-effort; a failed save must not break the app
        }
    }
}