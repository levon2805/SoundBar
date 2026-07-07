using System;
using System.IO;
using System.Security.Cryptography;

namespace UpdateSigner
{
    class Program
    {
        static void Main(string[] args)
        {
            string keyPath = "private.key";
            using RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048);

            if (File.Exists(keyPath))
            {
                // Load existing key
                string keyXml = File.ReadAllText(keyPath);
                rsa.FromXmlString(keyXml);
            }
            else
            {
                // Generate new key
                Console.WriteLine("Generating new RSA-2048 keypair...");
                string privateKeyXml = rsa.ToXmlString(true);
                string publicKeyXml = rsa.ToXmlString(false);

                File.WriteAllText(keyPath, privateKeyXml);
                
                Console.WriteLine("\n[IMPORTANT] A new private.key file has been generated.");
                Console.WriteLine("Do NOT upload this file to GitHub! Keep it secure.\n");
                
                Console.WriteLine("--- PUBLIC KEY (Copy this into UpdateService.cs) ---");
                Console.WriteLine(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(publicKeyXml)));
                Console.WriteLine("----------------------------------------------------\n");
            }

            if (args.Length > 0)
            {
                string targetFile = args[0];
                if (!File.Exists(targetFile))
                {
                    Console.WriteLine($"Error: File '{targetFile}' not found.");
                    return;
                }

                try
                {
                    Console.WriteLine($"Signing {targetFile}...");
                    byte[] fileBytes = File.ReadAllBytes(targetFile);
                    
                    // Hash and sign
                    using var sha256 = SHA256.Create();
                    byte[] hash = sha256.ComputeHash(fileBytes);
                    
                    RSAPKCS1SignatureFormatter formatter = new RSAPKCS1SignatureFormatter(rsa);
                    formatter.SetHashAlgorithm("SHA256");
                    byte[] signature = formatter.CreateSignature(hash);

                    // Write signature to .sig file
                    string sigPath = Path.ChangeExtension(targetFile, ".sig");
                    File.WriteAllText(sigPath, Convert.ToBase64String(signature));

                    Console.WriteLine($"Successfully created signature: {sigPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error signing file: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Usage: UpdateSigner.exe <path-to-update.zip>");
            }
        }
    }
}
