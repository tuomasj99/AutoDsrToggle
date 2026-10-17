// AutoDsrToggle - Display Resolution Toggler for NVIDIA DSR
// Copyright (C) [2026] [TuomasJ99]
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using NvAPIWrapper;
using NvAPIWrapper.Display;
using NvAPIWrapper.Native.Display;

class Program
{
#pragma warning disable SYSLIB1054 // Suppress P/Invoke source generation warnings

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetSystemMetrics(int nIndex);

#pragma warning restore SYSLIB1054

    // Helper method to fetch monitor marketing names and unique WMI hardware instance IDs
    private static List<(string Model, string InstanceId)> GetWmiMonitorIdentifiers()
    {
        var monitors = new List<(string, string)>();
        using var process = new Process();
        process.StartInfo.FileName = "powershell.exe";
        process.StartInfo.Arguments = "-NoProfile -Command \"Get-CimInstance -Namespace root\\wmi -ClassName WmiMonitorID | ForEach-Object { $r = $_.UserFriendlyName | Where-Object {$_ -ne 0}; if ($r) { $name = [System.Text.Encoding]::ASCII.GetString($r).Trim(); $inst = $_.InstanceName; Write-Output \\\"$name|$inst\\\" } }\"";
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("PowerShell WMI query process failed with exit code " + process.ExitCode);
        }

        foreach (string line in output.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('|');
            if (parts.Length >= 2)
            {
                monitors.Add((parts[0].Trim(), parts[1].Trim()));
            }
            else if (parts.Length == 1)
            {
                monitors.Add((parts[0].Trim(), "UnknownInstance"));
            }
        }

        return monitors;
    }

    // Helper method to write the companion readme file
    private static void CreateReadmeFile(string readmePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("============================================================");
        sb.AppendLine("AutoDsrToggle - Quick Guide");
        sb.AppendLine("============================================================");
        sb.AppendLine();
        sb.AppendLine("HOW IT WORKS (TOGGLE BEHAVIOR):");
        sb.AppendLine("AutoDsrToggle acts as a toggle switch between your native");
        sb.AppendLine("resolution and your configured DSR (Dynamic Super Resolution) target:");
        sb.AppendLine("- If your display is currently at NATIVE resolution, it switches to DSR.");
        sb.AppendLine("- If your display is currently at DSR resolution, it switches back to NATIVE.");
        sb.AppendLine("- If 'DsrMultiplier' is set to 1.0, the display stays at native resolution.");
        sb.AppendLine();
        sb.AppendLine("HOW TO CONFIGURE:");
        sb.AppendLine("1. Open 'adt_config.ini' in this folder.");
        sb.AppendLine("2. Locate your display section (e.g., [Display1]).");
        sb.AppendLine("3. Change 'DsrMultiplier=1.0' to your desired scaling factor (e.g., 1.25, 2.0, or 4.0).");
        sb.AppendLine("4. Save the file.");
        sb.AppendLine();
        sb.AppendLine("USAGE:");
        sb.AppendLine("- Run AutoDsrToggle (via shortcut, hotkey, or script) whenever you want");
        sb.AppendLine("  to toggle your display resolutions.");
        sb.AppendLine();
        sb.AppendLine("SAFETY & RELIABILITY FEATURES:");
        sb.AppendLine("- Single-Instance Lock: A system mutex prevents overlapping executions");
        sb.AppendLine("  and race conditions if the tool is triggered rapidly.");
        sb.AppendLine("- WMI Process Safeguards: Unique hardware identifiers track your specific monitors");
        sb.AppendLine("  even if display cables or ports are swapped.");
        sb.AppendLine("- Self-Healing: If a display order shift, invalid resolution or invalid config is detected,");
        sb.AppendLine("  the configuration automatically resets to defaults.");
        sb.AppendLine();
        sb.AppendLine("LICENSE:");
        sb.AppendLine("This program is free software released under the GNU General Public");
        sb.AppendLine("License v3.0 (GPL v3). You can find the full license text in the");
        sb.AppendLine("'gpl-3.0.txt' file located in this folder.");
        sb.AppendLine("============================================================");

        File.WriteAllText(readmePath, sb.ToString(), new UTF8Encoding(false));
    }

    // Helper method to extract the GPL v3 license file from embedded assembly resources
    private static void CreateLicenseFile(string licensePath)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            string? resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith("gpl-3.0.txt", StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream, new UTF8Encoding(false));
                    string content = reader.ReadToEnd();
                    File.WriteAllText(licensePath, content, new UTF8Encoding(false));
                    return;
                }
            }

            File.WriteAllText(licensePath, "GNU General Public License v3.0", new UTF8Encoding(false));
        }
        catch
        {
            File.WriteAllText(licensePath, "GNU General Public License v3.0", new UTF8Encoding(false));
        }
    }

    // Helper method to generate a fresh default configuration file with unique hardware identifiers
    private static void CreateDefaultConfig(string configPath, string readmePath, string licensePath, List<PathInfo> displayPaths, PropertyInfo? isPrimaryProp, PropertyInfo? resProp, PropertyInfo? refreshProp, List<(string Model, string InstanceId)> wmiMonitors)
    {
        if (wmiMonitors == null || wmiMonitors.Count == 0)
        {
            throw new InvalidOperationException("Fatal Error: WMI monitor identification failed. Critical hardware information is missing, preventing safe configuration generation.");
        }

        string? dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            CreateReadmeFile(readmePath);
            CreateLicenseFile(licensePath);
        }
        catch { }

        var sb = new StringBuilder();
        sb.AppendLine("; ==========================================================");
        sb.AppendLine("; AutoDsrToggle - Display Settings");
        sb.AppendLine("; Each active display is listed under its own section.");
        sb.AppendLine("; If DsrMultiplier is 1.0, the display is kept at native resolution.");
        sb.AppendLine("; ==========================================================");
        sb.AppendLine();

        int index = 1;
        foreach (var path in displayPaths)
        {
            if (path == null) continue;

            bool isPrimary = isPrimaryProp != null && Convert.ToBoolean(isPrimaryProp.GetValue(path));
            int nativeW = 0;
            int nativeH = 0;
            int refresh = 60;
            string monitorName = wmiMonitors.Count >= index ? wmiMonitors[index - 1].Model : "Unknown Display";
            string uniqueId = wmiMonitors.Count >= index ? wmiMonitors[index - 1].InstanceId : $"FallbackID_{index}";

            if (resProp != null)
            {
                object? resObj = resProp.GetValue(path);
                if (resObj != null)
                {
                    var resProps = resObj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    var widthProp = resProps.FirstOrDefault(p => p.Name.Contains("Width"));
                    var heightProp = resProps.FirstOrDefault(p => p.Name.Contains("Height"));

                    if (widthProp != null && heightProp != null)
                    {
                        nativeW = Convert.ToInt32(widthProp.GetValue(resObj));
                        nativeH = Convert.ToInt32(heightProp.GetValue(resObj));
                    }
                }
            }

            if (nativeW <= 0 || nativeH <= 0)
            {
                nativeW = GetSystemMetrics(0);
                nativeH = GetSystemMetrics(1);
            }

            if (refreshProp != null)
            {
                try { refresh = Convert.ToInt32(refreshProp.GetValue(path)); } catch { }
            }

            sb.AppendLine($"[Display{index}]");
            sb.AppendLine($"; Monitor Model: {monitorName}");
            sb.AppendLine($"UniqueId={uniqueId}");
            sb.AppendLine($"PrimaryDisplay={(isPrimary ? "Yes" : "No")}");
            sb.AppendLine($"NativeWidth={nativeW}");
            sb.AppendLine($"NativeHeight={nativeH}");
            sb.AppendLine("DsrMultiplier=1.0");
            sb.AppendLine($"RefreshRate={refresh}");
            sb.AppendLine();

            index++;
        }

        File.WriteAllText(configPath, sb.ToString(), new UTF8Encoding(false));
    }

    [SupportedOSPlatform("windows")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:ResolveMethodArgumentMismatch", Justification = "Safe unmanaged boundary inspection to resolve property naming drift.")]
    static void Main()
    {
        // Mutex concurrency lock to prevent overlapping executions and race conditions
        using var mutex = new Mutex(false, "Local\\AutoDsrToggle_SingleInstance_Mutex");
        bool hasOwnership = false;

        try
        {
            hasOwnership = mutex.WaitOne(TimeSpan.FromSeconds(5), false);
        }
        catch (AbandonedMutexException)
        {
            hasOwnership = true;
        }

        if (!hasOwnership)
        {
            return; // Exit silently if another instance is currently running
        }

        string appDataLocalDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoDsrToggle");
        string configPath = Path.Combine(appDataLocalDir, "adt_config.ini");
        string readmePath = Path.Combine(appDataLocalDir, "readme.txt");
        string licensePath = Path.Combine(appDataLocalDir, "gpl-3.0.txt");

        try
        {
            // 1. Initialize NVIDIA NVAPI environment
            NVIDIA.Initialize();
            System.Threading.Thread.Sleep(100);

            var displayPaths = PathInfo.GetDisplaysConfig().ToList();
            var properties = typeof(PathInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            bool isFirstRun = !File.Exists(configPath);

            var isPrimaryProp = properties.FirstOrDefault(p => p.Name.Contains("Primary") || p.Name.Contains("GDI"));
            var resProp = properties.FirstOrDefault(p => p.Name.Contains("Resolution") || p.Name.Contains("Mode"));
            var refreshProp = properties.FirstOrDefault(p => p.Name.Contains("Rate") || p.Name.Contains("Frequency") || p.Name.Contains("Hz"));

            // Fetch display identifiers via WMI (throws if WMI fails, triggering fatal error if during setup)
            List<(string Model, string InstanceId)> wmiMonitors;
            try
            {
                wmiMonitors = GetWmiMonitorIdentifiers();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Critical WMI failure: {ex.Message}");
            }

            // 2. IF CONFIG DOES NOT EXIST -> CREATE CONFIG, README & GPL V3 LICENSE FILES
            if (isFirstRun)
            {
                CreateDefaultConfig(configPath, readmePath, licensePath, displayPaths, isPrimaryProp, resProp, refreshProp, wmiMonitors);

                string welcomeMsg = $"Successfully created 'adt_config.ini', 'readme.txt' and 'gpl-3.0.txt'!\nMapped {displayPaths.Count} active display(s) via NVAPI.\n\n" +
                                    $"CONFIG PATH:\n{configPath}\n\n" +
                                    "Would you like to open the configuration folder now to view the guide, license, or edit your settings?";

                int result = MessageBoxW(IntPtr.Zero, welcomeMsg, "AutoDsrToggle Setup", 0x00000004 | 0x00000040);
                if (result == 6)
                {
                    Process.Start("explorer.exe", appDataLocalDir);
                }
                return;
            }

            // Ensure companion files exist even if config already existed
            if (!File.Exists(readmePath))
            {
                try { CreateReadmeFile(readmePath); } catch { }
            }
            if (!File.Exists(licensePath))
            {
                try { CreateLicenseFile(licensePath); } catch { }
            }

            // 3. READ SETTINGS FROM INI FILE AND VALIDATE UNIQUE HARDWARE IDENTIFIERS
            var screenConfigs = new Dictionary<int, (int width, int height, double multiplier, int refresh, string uniqueId)>();
            int maxExistingIndex = 0;

            if (File.Exists(configPath))
            {
                string[] lines = File.ReadAllLines(configPath, new UTF8Encoding(false));
                int currentIdx = -1;
                int w = 0, h = 0, refRate = 60;
                double mult = 1.0;
                string instanceId = string.Empty;

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#')) continue;

                    if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                    {
                        if (currentIdx != -1)
                        {
                            screenConfigs[currentIdx] = (w, h, mult, refRate, instanceId);
                            if (currentIdx > maxExistingIndex) maxExistingIndex = currentIdx;
                        }

                        string secName = trimmed[1..^1].Trim();
                        string digits = new([.. secName.Where(char.IsDigit)]);
                        if (int.TryParse(digits, out int parsedIdx))
                        {
                            currentIdx = parsedIdx;
                            w = 0; h = 0; mult = 1.0; refRate = 60; instanceId = string.Empty;
                        }
                        else
                        {
                            currentIdx = -1;
                        }
                        continue;
                    }

                    if (currentIdx != -1)
                    {
                        string[] kvp = trimmed.Split('=');
                        if (kvp.Length == 2)
                        {
                            string key = kvp[0].Trim();
                            string val = kvp[1].Trim();

                            if (key.Equals("UniqueId", StringComparison.OrdinalIgnoreCase))
                            {
                                instanceId = val;
                            }
                            else if (key.Equals("NativeWidth", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(val, out int parsedW)) w = parsedW;
                            }
                            else if (key.Equals("NativeHeight", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(val, out int parsedH)) h = parsedH;
                            }
                            else if (key.Equals("RefreshRate", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(val, out int parsedRef)) refRate = parsedRef;
                            }
                            else if (key.Equals("DsrMultiplier", StringComparison.OrdinalIgnoreCase))
                            {
                                if (double.TryParse(val.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedMult)) mult = parsedMult;
                            }
                        }
                    }
                }

                if (currentIdx != -1)
                {
                    screenConfigs[currentIdx] = (w, h, mult, refRate, instanceId);
                    if (currentIdx > maxExistingIndex) maxExistingIndex = currentIdx;
                }
            }

            // STRICT CONFIG STRUCTURE & RANGE VALIDATION:
            // Ensure config display count matches physical active display count exactly,
            // indexes start from 1, and there are no gaps or out-of-range indices.
            int physicalDisplayCount = displayPaths.Count;
            if (screenConfigs.Count != physicalDisplayCount)
            {
                throw new InvalidOperationException($"Configuration display count mismatch. Expected {physicalDisplayCount} display section(s), but found {screenConfigs.Count} in 'adt_config.ini'.");
            }

            for (int i = 1; i <= physicalDisplayCount; i++)
            {
                if (!screenConfigs.ContainsKey(i))
                {
                    throw new InvalidOperationException($"Configuration structural error: Missing expected section [Display{i}] in 'adt_config.ini'.");
                }
            }

            // SAFETY CHECK: Verify real-time WMI unique instance IDs against stored config IDs to detect display order shifts
            for (int i = 1; i <= screenConfigs.Count; i++)
            {
                if (screenConfigs.TryGetValue(i, out var cfg))
                {
                    string liveId = wmiMonitors.Count >= i ? wmiMonitors[i - 1].InstanceId : "UnknownInstance";
                    if (!string.IsNullOrEmpty(cfg.uniqueId) && !cfg.uniqueId.StartsWith("FallbackID") && !liveId.Equals("UnknownInstance"))
                    {
                        if (!cfg.uniqueId.Equals(liveId, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"Display order shift or hardware change detected at Display {i}. Expected unique hardware ID '{cfg.uniqueId}', but live hardware reported '{liveId}'.");
                        }
                    }
                }
            }

            // Scan current active displays and append any newly connected displays dynamically
            int pathIndex = 1;
            bool newDisplayAdded = false;
            var sbNewDisplays = new StringBuilder();

            foreach (var path in displayPaths)
            {
                if (path == null) continue;

                if (pathIndex > maxExistingIndex)
                {
                    bool isPrimary = isPrimaryProp != null && Convert.ToBoolean(isPrimaryProp.GetValue(path));
                    int nativeW = 0;
                    int nativeH = 0;
                    int refresh = 60;
                    string monitorName = wmiMonitors.Count >= pathIndex ? wmiMonitors[pathIndex - 1].Model : "Unknown Display";
                    string uniqueId = wmiMonitors.Count >= pathIndex ? wmiMonitors[pathIndex - 1].InstanceId : $"FallbackID_{pathIndex}";

                    if (resProp != null)
                    {
                        object? resObj = resProp.GetValue(path);
                        if (resObj != null)
                        {
                            var resProps = resObj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                            var widthProp = resProps.FirstOrDefault(p => p.Name.Contains("Width"));
                            var heightProp = resProps.FirstOrDefault(p => p.Name.Contains("Height"));

                            if (widthProp != null && heightProp != null)
                            {
                                nativeW = Convert.ToInt32(widthProp.GetValue(resObj));
                                nativeH = Convert.ToInt32(heightProp.GetValue(resObj));
                            }
                        }
                    }

                    if (nativeW <= 0 || nativeH <= 0)
                    {
                        nativeW = GetSystemMetrics(0);
                        nativeH = GetSystemMetrics(1);
                    }

                    if (refreshProp != null)
                    {
                        try { refresh = Convert.ToInt32(refreshProp.GetValue(path)); } catch { }
                    }

                    sbNewDisplays.AppendLine();
                    sbNewDisplays.AppendLine($"[Display{pathIndex}]");
                    sbNewDisplays.AppendLine($"; Monitor Model: {monitorName}");
                    sbNewDisplays.AppendLine($"UniqueId={uniqueId}");
                    sbNewDisplays.AppendLine($"PrimaryDisplay={(isPrimary ? "Yes" : "No")}");
                    sbNewDisplays.AppendLine($"NativeWidth={nativeW}");
                    sbNewDisplays.AppendLine($"NativeHeight={nativeH}");
                    sbNewDisplays.AppendLine("DsrMultiplier=1.0");
                    sbNewDisplays.AppendLine($"RefreshRate={refresh}");

                    screenConfigs[pathIndex] = (nativeW, nativeH, 1.0, refresh, uniqueId);
                    newDisplayAdded = true;
                }

                pathIndex++;
            }

            if (newDisplayAdded)
            {
                File.AppendAllText(configPath, sbNewDisplays.ToString(), new UTF8Encoding(false));
            }

            // 4. REGULAR EXECUTION: TOGGLE BETWEEN NATIVE AND DSR RESOLUTIONS WITH PER-DEVICE VALIDATION
            bool configUpdated = false;
            pathIndex = 1;

            foreach (var path in displayPaths)
            {
                if (path == null) continue;

                if (!screenConfigs.TryGetValue(pathIndex, out var cfg))
                {
                    pathIndex++;
                    continue;
                }

                int nativeWidth = cfg.width;
                int nativeHeight = cfg.height;
                double dsrMultiplier = cfg.multiplier;
                int forcedRefreshRate = cfg.refresh;

                // If multiplier is 1.0, keep display at native resolution and skip changes
                if (Math.Abs(dsrMultiplier - 1.0) < 0.001 || nativeWidth <= 0 || nativeHeight <= 0)
                {
                    pathIndex++;
                    continue;
                }

                // Calculate DSR target resolution using the square root rule
                double multiplierRoot = Math.Sqrt(dsrMultiplier);
                int targetWidth = (int)Math.Round(nativeWidth * multiplierRoot);
                int targetHeight = (int)Math.Round(nativeHeight * multiplierRoot);

                // Per-device validation: compare allowed/native baseline resolution against set target resolution
                if (targetWidth <= nativeWidth || targetHeight <= nativeHeight || targetWidth > nativeWidth * 5 || targetHeight > nativeHeight * 5)
                {
                    throw new InvalidOperationException($"The set resolution ({targetWidth}x{targetHeight}) for Display {pathIndex} is invalid or exceeds allowed limits compared to its native baseline ({nativeWidth}x{nativeHeight}).");
                }

                if (resProp != null)
                {
                    object? resObj = resProp.GetValue(path);
                    if (resObj != null)
                    {
                        var resProps = resObj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                        var widthProp = resProps.FirstOrDefault(p => p.Name.Contains("Width"));

                        if (widthProp != null)
                        {
                            int currentWidth = Convert.ToInt32(widthProp.GetValue(resObj));
                            object targetResolution;

                            if (currentWidth > nativeWidth)
                            {
                                // Currently at DSR -> Revert back to native resolution
                                if (resProp.PropertyType.Name.Contains("Size"))
                                {
                                    targetResolution = new System.Drawing.Size(nativeWidth, nativeHeight);
                                }
                                else
                                {
                                    var ctor = resProp.PropertyType.GetConstructor([typeof(int), typeof(int), typeof(int)]);
                                    targetResolution = ctor!.Invoke([nativeWidth, nativeHeight, 32]);
                                }
                            }
                            else
                            {
                                // Currently at Native -> Scale up to DSR resolution
                                if (resProp.PropertyType.Name.Contains("Size"))
                                {
                                    targetResolution = new System.Drawing.Size(targetWidth, targetHeight);
                                }
                                else
                                {
                                    var ctor = resProp.PropertyType.GetConstructor([typeof(int), typeof(int), typeof(int)]);
                                    targetResolution = ctor!.Invoke([targetWidth, targetHeight, 32]);
                                }
                            }

                            resProp.SetValue(path, targetResolution);
                        }
                    }
                }

                if (refreshProp != null && forcedRefreshRate > 0)
                {
                    refreshProp.SetValue(path, Convert.ChangeType(forcedRefreshRate, refreshProp.PropertyType));
                }

                configUpdated = true;
                pathIndex++;
            }

            // 5. COMMIT CONFIGURATION CHANGES TO THE GPU VIA NVAPI AND VERIFY EFFECT
            if (configUpdated)
            {
                var setMethod = typeof(PathInfo).GetMethod("SetDisplaysConfig", BindingFlags.Public | BindingFlags.Static);
                if (setMethod != null)
                {
                    var parameters = setMethod.GetParameters();

                    if (parameters.Length == 1)
                    {
                        setMethod.Invoke(null, [displayPaths.ToArray()]);
                    }
                    else
                    {
                        var defaultFlag = Enum.ToObject(parameters[1].ParameterType, 0);
                        setMethod.Invoke(null, [displayPaths.ToArray(), defaultFlag]);
                    }
                }

                // POST-EXECUTION VERIFICATION: Check if changes took effect properly
                Thread.Sleep(300);
                var verificationPaths = PathInfo.GetDisplaysConfig().ToList();
                if (verificationPaths == null || verificationPaths.Count == 0)
                {
                    throw new FormatException("Post-execution verification failed: Display configuration paths could not be verified after applying changes.");
                }
            }
        }
        catch (FormatException ex)
        {
            // UNIQUE ERROR FOR VERIFICATION OR NON-CRITICAL RUNTIME FAILURES -> DO NOT RESET CONFIG
            string warningMsg = "AutoDsrToggle Verification Warning:\n\n" +
                                $"{ex.Message}\n\n" +
                                "The resolution change could not be fully verified or completed. Your configuration file 'adt_config.ini' has been preserved and was NOT reset.";
            _ = MessageBoxW(IntPtr.Zero, warningMsg, "AutoDsrToggle Warning - Config Preserved", 0x00000030); // MB_ICONWARNING
        }
        catch (Exception ex)
        {
            // SAFETY CHECK: If validation fails, order shift is detected, or driver rejects config, delete old config and regenerate fresh defaults immediately
            try
            {
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }
                if (File.Exists(readmePath))
                {
                    File.Delete(readmePath);
                }
                if (File.Exists(licensePath))
                {
                    File.Delete(licensePath);
                }

                NVIDIA.Initialize();
                var displayPaths = PathInfo.GetDisplaysConfig().ToList();
                var properties = typeof(PathInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var isPrimaryProp = properties.FirstOrDefault(p => p.Name.Contains("Primary") || p.Name.Contains("GDI"));
                var resProp = properties.FirstOrDefault(p => p.Name.Contains("Resolution") || p.Name.Contains("Mode"));
                var refreshProp = properties.FirstOrDefault(p => p.Name.Contains("Rate") || p.Name.Contains("Frequency") || p.Name.Contains("Hz"));
                var wmiMonitors = GetWmiMonitorIdentifiers();

                CreateDefaultConfig(configPath, readmePath, licensePath, displayPaths, isPrimaryProp, resProp, refreshProp, wmiMonitors);
            }
            catch (Exception genEx)
            {
                // FATAL ERROR if WMI/Config generation fails critically
                string fatalMsg = "FATAL ERROR: AutoDsrToggle failed to generate or reset the configuration file because critical WMI hardware information is missing.\n\n" +
                                  $"Details: {genEx.Message}";
                _ = MessageBoxW(IntPtr.Zero, fatalMsg, "AutoDsrToggle Fatal Error", 0x00000010); // MB_ICONERROR
                return;
            }

            string errorMsg = "A display order shift, hardware change, structural config error, unsupported resolution, or invalid configuration was detected.\n\n" +
                              $"Details: {ex.Message}\n\n" +
                              "The configuration file 'adt_config.ini' has been reset and regenerated with default settings.\n\n" +
                              "Would you like to open the configuration folder now?";

            int errResult = MessageBoxW(IntPtr.Zero, errorMsg, "AutoDsrToggle Error - Config Reset", 0x00000004 | 0x00000010);
            if (errResult == 6)
            {
                Process.Start("explorer.exe", appDataLocalDir);
            }
        }
        finally
        {
            if (hasOwnership)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch { }
            }
        }
    }
}