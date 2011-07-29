﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet.Security;
using System.Security.Cryptography;
using System.Security;
using Renci.SshNet.Common;
using System.Globalization;
using Renci.SshNet.Security.Cryptography;

namespace Renci.SshNet
{
    /// <summary>
    /// old private key information/
    /// </summary>
    public class PrivateKeyFile
    {
#if SILVERLIGHT
        private static Regex _privateKeyRegex = new Regex(@"^-----BEGIN (?<keyName>\w+) PRIVATE KEY-----\r?\n(Proc-Type: 4,ENCRYPTED\r?\nDEK-Info: (?<cipherName>[A-Z0-9-]+),(?<salt>[A-F0-9]{16})\r?\n\r?\n)?(?<data>([a-zA-Z0-9/+=]{1,64}\r?\n)+)-----END \k<keyName> PRIVATE KEY-----.*", RegexOptions.Multiline);
#else
        private static Regex _privateKeyRegex = new Regex(@"^-----BEGIN (?<keyName>\w+) PRIVATE KEY-----\r?\n(Proc-Type: 4,ENCRYPTED\r?\nDEK-Info: (?<cipherName>[A-Z0-9-]+),(?<salt>[A-F0-9]{16})\r?\n\r?\n)?(?<data>([a-zA-Z0-9/+=]{1,64}\r?\n)+)-----END \k<keyName> PRIVATE KEY-----.*", RegexOptions.Compiled | RegexOptions.Multiline);
#endif

        private CryptoPrivateKey _key;

        /// <summary>
        /// Gets the name of private key algorithm.
        /// </summary>
        /// <value>
        /// The name of the algorithm.
        /// </value>
        public string AlgorithmName
        {
            get
            {
                return this._key.Name;
            }
        }

        /// <summary>
        /// Gets the public key.
        /// </summary>
        public byte[] PublicKey
        {
            get
            {
                return this._key.GetPublicKey().GetBytes().ToArray();
            }
        }

        /// <summary>
        /// Gets the signature.
        /// </summary>
        /// <param name="sessionId">The session id.</param>
        /// <returns>Signature data</returns>
        public byte[] GetSignature(IEnumerable<byte> sessionId)
        {
            return this._key.GetSignature(sessionId);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrivateKeyFile"/> class.
        /// </summary>
        /// <param name="privateKey">The private key.</param>
        public PrivateKeyFile(Stream privateKey)
        {
            this.Open(privateKey, null);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrivateKeyFile"/> class.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        public PrivateKeyFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentNullException("fileName");

            using (var keyFile = File.Open(fileName, FileMode.Open))
            {
                this.Open(keyFile, null);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrivateKeyFile"/> class.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <param name="passPhrase">The pass phrase.</param>
        public PrivateKeyFile(string fileName, string passPhrase)
        {
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentNullException("fileName");

            using (var keyFile = File.Open(fileName, FileMode.Open))
            {
                this.Open(keyFile, passPhrase);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrivateKeyFile"/> class.
        /// </summary>
        /// <param name="privateKey">The private key.</param>
        /// <param name="passPhrase">The pass phrase.</param>
        public PrivateKeyFile(Stream privateKey, string passPhrase)
        {
            this.Open(privateKey, passPhrase);
        }

        /// <summary>
        /// Opens the specified private key.
        /// </summary>
        /// <param name="privateKey">The private key.</param>
        /// <param name="passPhrase">The pass phrase.</param>
        private void Open(Stream privateKey, string passPhrase)
        {
            if (privateKey == null)
                throw new ArgumentNullException("privateKey");

            Match privateKeyMatch = null;

            using (StreamReader sr = new StreamReader(privateKey))
            {
                var text = sr.ReadToEnd();
                privateKeyMatch = _privateKeyRegex.Match(text);
            }

            if (!privateKeyMatch.Success)
            {
                throw new SshException("Invalid private key file.");
            }

            var keyName = privateKeyMatch.Result("${keyName}");
            var cipherName = privateKeyMatch.Result("${cipherName}");
            var salt = privateKeyMatch.Result("${salt}");
            var data = privateKeyMatch.Result("${data}");

            var binaryData = System.Convert.FromBase64String(data);

            IEnumerable<byte> decryptedData;

            if (!string.IsNullOrEmpty(cipherName) && !string.IsNullOrEmpty(salt))
            {
                if (string.IsNullOrEmpty(passPhrase))
                    throw new SshPassPhraseNullOrEmptyException("Private key is encrypted but passphrase is empty.");

                byte[] binarySalt = new byte[salt.Length / 2];
                for (int i = 0; i < binarySalt.Length; i++)
                    binarySalt[i] = Convert.ToByte(salt.Substring(i * 2, 2), 16);

                Cipher cipher = null;
                switch (cipherName)
                {
                    case "DES-EDE3-CBC":
                        cipher = new CipherTripleDes192Cbc();
                        break;
                    case "DES-CBC":
                        //  TODO:   Not tested
                        cipher = new CipherDes64Cbc();
                        break;
                    case "AES-128-CBC":
                        //  TODO:   Not tested
                        cipher = new CipherAes128Cbc();
                        break;
                    case "AES-192-CBC":
                        //  TODO:   Not tested
                        cipher = new CipherAes192Cbc();
                        break;
                    case "AES-256-CBC":
                        //  TODO:   Not tested
                        cipher = new CipherAes256Cbc();
                        break;
                    default:
                        throw new SshException(string.Format(CultureInfo.CurrentCulture, "Unknown private key cipher \"{0}\".", cipherName));
                }

                decryptedData = DecryptKey(cipher, binaryData, passPhrase, binarySalt);
            }
            else
            {
                decryptedData = binaryData;
            }

            switch (keyName)
            {
                case "RSA":
                    this._key = new CryptoPrivateKeyRsa();
                    break;
#if SILVERLIGHT
#else
                case "DSA":
                    this._key = new CryptoPrivateKeyDss();
                    break;
#endif
                default:
                    throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, "Key '{0}' is not supported.", keyName));
            }

            this._key.Load(decryptedData);
        }

        /// <summary>
        /// Decrypts encrypted private key file data.
        /// </summary>
        /// <param name="cipher">Encryption cipher.</param>
        /// <param name="cipherData">Encrypted data.</param>
        /// <param name="passPhrase">Decryption pass phrase.</param>
        /// <param name="binarySalt">Decryption binary salt.</param>
        /// <returns></returns>
        public static IEnumerable<byte> DecryptKey(Cipher cipher, byte[] cipherData, string passPhrase, byte[] binarySalt)
        {
            List<byte> cipherKey = new List<byte>();

            using (var md5 = new MD5Hash())
            {
                var passwordBytes = Encoding.UTF8.GetBytes(passPhrase);

                var initVector = passwordBytes.Concat(binarySalt);

                var hash = md5.ComputeHash(initVector.ToArray()).AsEnumerable();

                cipherKey.AddRange(hash);

                while (cipherKey.Count < cipher.KeySize / 8)
                {
                    hash = hash.Concat(initVector);

                    hash = md5.ComputeHash(hash.ToArray());

                    cipherKey.AddRange(hash);
                }
            }

            cipher.Init(cipherKey, binarySalt);

            return cipher.Decrypt(cipherData);
        }
    }
}