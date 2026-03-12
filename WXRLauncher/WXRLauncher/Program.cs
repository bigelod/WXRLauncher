using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using PeNet;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.ComponentModel.Design;

namespace WXRLauncher
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //------------------------------------
            //CHANGE HERE TO ADJUST AS NEEDED -> Source DLL (relative to startup path)
            //------------------------------------
            const string copyD3DDLL = "dxgi.dll";

            //Parameter variables
            string launchEXEPath = "";
            string forcedD3DVersion = "";
            string forcedRelativePath = "";
            bool noPurge = false;
            string forceLaunchParam = "";
            bool debug = false;
            bool advancedDebug = false;

            List<string> argsList = args.ToList();

            int debugIndex = argsList.IndexOf("-debug");
            if (debugIndex != -1)
            {
                debug = true;

                argsList.RemoveAt(debugIndex);
            }

            int debugVerboseIndex = argsList.IndexOf("-verbose");
            if (debugVerboseIndex != -1)
            {
                advancedDebug = true;

                argsList.RemoveAt(debugVerboseIndex);
            }

            int noPurgeIndex = argsList.IndexOf("-force-no-purge");
            if (noPurgeIndex != -1)
            {
                noPurge = true;

                argsList.RemoveAt(noPurgeIndex);
            }

            int forceFlagIndex = argsList.IndexOf("-force-dx-version");
            if (forceFlagIndex != -1)
            {
                if (forceFlagIndex + 1 < argsList.Count)
                {
                    forcedD3DVersion = argsList[forceFlagIndex + 1];

                    argsList.RemoveAt(forceFlagIndex + 1);
                }
                else
                {
                    Console.WriteLine("Error: -force-dx-version requires a DLL name argument. Ignoring it.");
                }

                argsList.RemoveAt(forceFlagIndex);
            }

            int relativePathIndex = argsList.IndexOf("-force-relative-path");
            if (relativePathIndex != -1)
            {
                if (relativePathIndex + 1 < argsList.Count)
                {
                    forcedRelativePath = argsList[relativePathIndex + 1];

                    argsList.RemoveAt(relativePathIndex + 1);
                }
                else
                {
                    Console.WriteLine("Error: -force-relative-path requires a path argument. Ignoring it.");
                }

                argsList.RemoveAt(relativePathIndex);
            }

            int launchParamIndex = argsList.IndexOf("-force-launch-param");
            if (launchParamIndex != -1)
            {
                if (launchParamIndex + 1 < argsList.Count)
                {
                    forceLaunchParam = argsList[launchParamIndex + 1];

                    argsList.RemoveAt(launchParamIndex + 1);
                }
                else
                {
                    Console.WriteLine("Error: -force-launch-param requires a launch parameter provided. Ignoring it.");
                }

                argsList.RemoveAt(launchParamIndex);
            }

            if (argsList.Count == 1)
            {
                launchEXEPath = argsList[0];
            }
            else
            {
                Console.WriteLine("Usage: WXRLauncher.exe <path-to-exe>");
                Console.WriteLine("Optional flags:");
                Console.WriteLine("-force-dx-version <dll-name> :: Force the launcher to treat it as a given DirectX version (D3D9, D3D10, D3D11, D3D12)");
                Console.WriteLine("-force-relative-path <relative-path> :: Force the launcher to inject into this path relative to the given EXE to launch");
                Console.WriteLine("-force-no-purge :: Prevent cleanup of any existing D3D DLL files");
                Console.WriteLine("-force-launch-param <launch-parameters> :: Force the EXE to launch with the given parameter (eg: to force D3D11)");
                Console.WriteLine("-debug :: Show debug data");
                if (debug)
                {
                    Console.WriteLine("Manually enter a path to check:");
                    Console.WriteLine();
                    Console.Write("EXE Path To Check (Or Blank To Quit): ");
                    launchEXEPath = Console.ReadLine();
                }
                else
                {
                    Console.WriteLine("Press Enter To Exit...");
                    Console.ReadLine();
                }
            }

            if (launchEXEPath != "")
            {
                if (debug) Console.WriteLine("Searching for DirectX calls in EXE...");

                if (!File.Exists(launchEXEPath))
                {
                    Console.WriteLine("Error! File not found: " + launchEXEPath);
                }
                else
                {
                    try
                    {
                        PeFile pe = new PeFile(launchEXEPath);
                        HashSet<string> importDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        if (pe.ImportedFunctions != null)
                        {
                            foreach (PeNet.Header.Pe.ImportFunction import in pe.ImportedFunctions)
                            {
                                if (!string.IsNullOrEmpty(import.DLL))
                                {
                                    importDlls.Add(import.DLL);
                                }
                            }
                        }

                        if (pe.DelayImportedFunctions != null)
                        {
                            foreach (PeNet.Header.Pe.ImportFunction import in pe.DelayImportedFunctions)
                            {
                                if (!string.IsNullOrEmpty(import.DLL))
                                {
                                    importDlls.Add(import.DLL);
                                }
                            }
                        }

                        //------------------------------------
                        //CHANGE HERE TO ADJUST AS NEEDED -> Detection mapping
                        //------------------------------------
                        Dictionary<string, string[]> d3dMap = new Dictionary<string, string[]>
                        {
                            ["Unreal Engine 2?"] = new[] { "engine.dll" },
                            ["Unity"] = new[] { "unityplayer.dll" },
                            ["D3D8"] = new[] { "d3d8.dll" },
                            ["D3D9"] = new[] { "d3d9.dll" },
                            ["D3D10"] = new[] { "d3d10.dll", "d3d10_1.dll" },
                            ["D3D11"] = new[] { "d3d11.dll" },
                            ["D3D12"] = new[] { "d3d12.dll" },
                        };

                        if (debug) Console.WriteLine("File: " + launchEXEPath);

                        bool foundAny = false;
                        string highestFound = "";
                        foreach (KeyValuePair<string, string[]> kv in d3dMap)
                        {
                            List<string> foundDlls = kv.Value.Where(dll => importDlls.Contains(dll)).ToList();
                            if (foundDlls.Any())
                            {
                                if (debug) Console.WriteLine(kv.Key.PadRight(6) + ": yes");
                                foreach (string dll in foundDlls)
                                {
                                    if (debug) Console.WriteLine($" - found {dll}");
                                }
                                highestFound = kv.Key;
                                foundAny = true;
                            }
                            else
                            {
                                if (debug) Console.WriteLine(kv.Key.PadRight(6) + ": no");
                            }
                        }
                        if (!foundAny && forcedD3DVersion == "")
                        {
                            if (debug) Console.WriteLine("\nNo Direct3D DLLs found in import tables (may load dynamically at runtime).");
                        }
                        else if (highestFound != "" || forcedD3DVersion != "")
                        {
                            string targetD3DDLL = "dxgi.dll";

                            if (forcedD3DVersion != "")
                            {
                                if (debug) Console.WriteLine("Forced Direct3D Version: " + forcedD3DVersion);
                                targetD3DDLL = forcedD3DVersion;
                            }
                            else
                            {
                                //Determine which D3D version to use
                                //------------------------------------
                                //CHANGE HERE TO ADJUST AS NEEDED -> Inject actions
                                //------------------------------------
                                switch (highestFound.ToUpper())
                                {
                                    case "UNREAL ENGINE 2?":
                                        targetD3DDLL = "d3d9.dll";
                                        break;
                                    case "UNITY":
                                        targetD3DDLL = "d3d11.dll";

                                        //Try to force d3d11 too for Unity
                                        if (forceLaunchParam == "")
                                        {
                                            forceLaunchParam = "-force-d3d11";
                                        }
                                        else if (!forceLaunchParam.Contains("-force-d3d11"))
                                        {
                                            forceLaunchParam += " -force-d3d11";
                                        }
                                        break;
                                    case "D3D8":
                                        targetD3DDLL = "d3d8.dll";
                                        break;
                                    case "D3D9":
                                        targetD3DDLL = "d3d9.dll";
                                        break;
                                    case "D3D11":
                                        targetD3DDLL = "d3d11.dll";
                                        break;
                                    case "D3D12":
                                        targetD3DDLL = "d3d12.dll";
                                        break;
                                }
                            }

                            bool purgeSuccess = false;
                            string actualPurgePath = Path.GetDirectoryName(launchEXEPath);

                            if (forcedRelativePath != "")
                            {
                                actualPurgePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(launchEXEPath), forcedRelativePath));
                            }

                            if (!Directory.Exists(actualPurgePath))
                            {
                                purgeSuccess = false;
                            }

                            if (!noPurge)
                            {
                                if (debug) Console.WriteLine("Purging any existing Direct3D DLLs found in directory: " + actualPurgePath);

                                //------------------------------------
                                //CHANGE HERE TO ADJUST AS NEEDED -> Purge List
                                //------------------------------------
                                string[] purgeList = { "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll" };

                                string[] foundDLLs = Directory.GetFiles(actualPurgePath, "*.dll");

                                foreach (string foundDLL in foundDLLs)
                                {
                                    if (purgeList.Contains(Path.GetFileName(foundDLL).ToLower()))
                                    {
                                        if (debug && advancedDebug) Console.WriteLine("DEBUG: Deleting " + Path.GetFileName(foundDLL));
                                        File.Delete(foundDLL);
                                    }
                                }

                                purgeSuccess = true;
                            }
                            else
                            {
                                if (debug) Console.WriteLine("Skipping purge...");
                                purgeSuccess = true;
                            }

                            if (purgeSuccess)
                            {
                                bool injectSuccess = false;
                                if (debug) Console.WriteLine("Attempting to inject Direct3D: " + targetD3DDLL);

                                string copyFrom = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), copyD3DDLL);
                                string copyTo = Path.Combine(actualPurgePath, targetD3DDLL);

                                if (debug && advancedDebug)
                                {
                                    Console.WriteLine("DEBUG: Copying From: " + copyFrom);
                                    Console.WriteLine("DEBUG: Copying To: " + copyTo);
                                }

                                if (File.Exists(copyFrom))
                                {
                                    if (File.Exists(copyTo))
                                    {
                                        if (debug && advancedDebug) Console.WriteLine("DEBUG: Overwriting: " + copyTo);
                                        File.Delete(copyTo);
                                    }

                                    File.Copy(copyFrom, copyTo);
                                    injectSuccess = true;
                                }

                                if (injectSuccess)
                                {
                                    string launchTarget = launchEXEPath;

                                    if (forceLaunchParam != "")
                                    {
                                        launchTarget = launchEXEPath + " " + forceLaunchParam;
                                    }

                                    if (debug)
                                    {
                                        Console.WriteLine("Launching: " + launchTarget);

                                        if (advancedDebug)
                                        {
                                            Console.Write("DEBUG: Actually launch? [y/N]: ");
                                            string ans = Console.ReadLine();

                                            if (ans == "" || ans.ToUpper().Substring(0, 1) != "Y")
                                            {
                                                return;
                                            }
                                        }
                                    }
                                    
                                    //Wait for the launched EXE to exit so WinlatorXR doesn't think we're done early and quit to main menu
                                    Process.Start(launchEXEPath, forceLaunchParam).WaitForExit();
                                }
                                else
                                {
                                    if (debug) Console.WriteLine("Error! Unable to inject.");
                                }
                            }
                            else
                            {
                                if (debug) Console.WriteLine("Error! Unable to purge.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error: " + ex.Message);
                    }
                }
            }
        }
    }
}
