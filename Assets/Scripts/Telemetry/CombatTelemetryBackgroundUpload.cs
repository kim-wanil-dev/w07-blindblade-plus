using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlindBlade.Telemetry
{
    /// <summary>Windows 게임 종료 뒤 현재 실행의 로그만 전송하는 독립 업로더를 시작한다.</summary>
    internal static class CombatTelemetryBackgroundUpload
    {
        private const string REQUEST_PATTERN = "background-upload-*.request.json";
        private const string SCRIPT_NAME = "Upload-PendingCombatTelemetry.ps1";

        /// <summary>
        /// 새 업로더가 남긴 요청 파일만 다시 실행한다.
        /// root를 사용하며 과거 JSONL 전체를 전송 대상으로 추가하지 않고 시작 오류를 반환한다.
        /// </summary>
        internal static string ResumePending(string root)
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer) return null;
            try
            {
                foreach (string requestPath in Directory.GetFiles(root, REQUEST_PATTERN)) StartProcess(requestPath);
                return null;
            }
            catch (Exception exception)
            {
                return "종료 후 로그 전송 재개 실패: " + exception.GetType().Name;
            }
        }

        /// <summary>
        /// 현재 실행의 세션과 전송 설정을 디스크에 저장하고 숨김 업로더를 실행한다.
        /// root·configuration·sessionIds·배치 설정을 사용하며 시작 성공 여부와 준비 파일 경로 또는 오류를 반환한다.
        /// </summary>
        internal static bool TrySchedule(string root, JObject configuration, HashSet<string> sessionIds,
            int batchLimit, int timeout, out string readyPath, out string error)
        {
            readyPath = null;
            error = null;
            if (Application.platform != RuntimePlatform.WindowsPlayer || sessionIds.Count == 0
                || configuration.Value<bool?>("enabled") != true) return false;
            try
            {
                string jobId = Guid.NewGuid().ToString("N");
                string requestPath = Path.Combine(root, "background-upload-" + jobId + ".request.json");
                readyPath = requestPath + ".ready";
                using (System.Diagnostics.Process parent = System.Diagnostics.Process.GetCurrentProcess())
                {
                    JObject request = new JObject
                    {
                        ["root"] = root,
                        ["parentProcessId"] = parent.Id,
                        ["parentStartTicks"] = parent.StartTime.ToUniversalTime().Ticks,
                        ["configuration"] = configuration.DeepClone(),
                        ["sessionIds"] = new JArray(sessionIds),
                        ["batchLimit"] = batchLimit,
                        ["timeoutSeconds"] = Math.Max(60, timeout)
                    };
                    byte[] bytes = new UTF8Encoding(false).GetBytes(request.ToString(Formatting.None));
                    using (FileStream stream = new FileStream(requestPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                }
                StartProcess(requestPath);
                return true;
            }
            catch (Exception exception)
            {
                readyPath = null;
                error = "종료 후 로그 전송기 시작 실패: " + exception.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// StreamingAssets의 업로더를 별도 Windows PowerShell 프로세스로 실행한다.
        /// requestPath를 인수로 전달하며 게임 창이 닫혀도 자식 프로세스의 실행을 유지한다.
        /// </summary>
        private static void StartProcess(string requestPath)
        {
            string scriptPath = Path.Combine(Application.streamingAssetsPath, "Telemetry", SCRIPT_NAME);
            if (!File.Exists(scriptPath)) throw new FileNotFoundException("로그 전송 스크립트가 빌드에 없습니다.");
            string powershellPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = powershellPath,
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \""
                    + scriptPath + "\" -RequestPath \"" + requestPath + "\"",
                WorkingDirectory = Path.GetDirectoryName(requestPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo))
            {
                if (process == null) throw new IOException("로그 전송 프로세스를 시작하지 못했습니다.");
            }
        }
    }
}
