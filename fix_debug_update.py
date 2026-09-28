import sys

path = 'src/SoundBar/Services/UpdateService.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_block = '''                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            }
        }'''

new_block = '''                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            }
#if DEBUG
            // Force update available in debug mode for UI testing
            UpdateAvailable = true;
            LatestVersion = "v4.0.1-test";
#endif
        }'''

text = text.replace(old_block, new_block)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed Update check for debug')
