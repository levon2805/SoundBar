import sys
import re

path = 'src/SoundBar/Services/AudioDeviceSwitcher.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

pattern = re.compile(r'public static void SetDefaultDevice\(string deviceId\).*?catch\s*\{\s*// Silently fail if COM is unavailable on this system\s*\}', re.DOTALL)

new_code = '''public static void SetDefaultDevice(string deviceId)
        {
            object? pcc = null;
            try
            {
                pcc = new PolicyConfigClient();
                
                // Try Windows 10/11 version
                if (pcc is IPolicyConfig config)
                {
                    config.SetDefaultEndpoint(deviceId, 0); // eConsole
                    config.SetDefaultEndpoint(deviceId, 1); // eMultimedia
                    config.SetDefaultEndpoint(deviceId, 2); // eCommunications
                    return;
                }

                // Try alternate Windows 11/Vista version
                if (pcc is IPolicyConfigVista configVista)
                {
                    configVista.SetDefaultEndpoint(deviceId, 0);
                    configVista.SetDefaultEndpoint(deviceId, 1);
                    configVista.SetDefaultEndpoint(deviceId, 2);
                    return;
                }

                // Try classic Windows 7/8 version
                if (pcc is IPolicyConfigClassic configClassic)
                {
                    configClassic.SetDefaultEndpoint(deviceId, 0);
                    configClassic.SetDefaultEndpoint(deviceId, 1);
                    configClassic.SetDefaultEndpoint(deviceId, 2);
                    return;
                }
            }
            catch
            {
                // Silently fail if COM is unavailable on this system
            }
            finally
            {
                if (pcc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(pcc);
            }'''

if pattern.search(text):
    text = pattern.sub(new_code, text)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(text)
    print('Success')
else:
    print('Not found')
