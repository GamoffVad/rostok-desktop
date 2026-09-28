using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Rostok.Licensing;

// Лицензия «Ростка», привязанная к компьютеру.
//   1. Установщик по кнопке «Сгенерировать ключ» считает ключ компьютера по данным материнской платы.
//   2. Администратор вставляет ключ в генератор; тот подписывает его секретным ключом (ECDSA P-256) — это серийный номер.
//   3. Установщик и программа проверяют подпись открытым ключом: подделать номер без секретного ключа нельзя,
//      а номер одного компьютера не подходит к другому.
public static class License
{
    public const string Product = "ROSTOK-1";

    // Открытый ключ проверки. Секретная половина — keys/rostok-license.private.pem, хранится только у администратора.
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAECBrW/BHOrZc+wEI2p2M3p42Gidml
        SqYl9V4tKOlm1EkyFUJJOkreR15/3PD84owRH7EAnFGQEXaaRzytZeGk/A==
        -----END PUBLIC KEY-----
        """;

    // ── Ключ компьютера ─────────────────────────────────
    // Производитель, модель и серийный номер материнской платы + UUID системной платы (SMBIOS).
    public static string MachineFingerprint()
    {
        var parts = new List<string>();
        try
        {
            using var board = new ManagementObjectSearcher("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard");
            foreach (var o in board.Get())
                parts.Add($"{o["Manufacturer"]}|{o["Product"]}|{o["SerialNumber"]}".Trim());
            using var product = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (var o in product.Get())
                parts.Add($"{o["UUID"]}".Trim());
        }
        catch (Exception) { /* WMI недоступен — ниже запасной идентификатор */ }
        if (parts.All(p => p.Replace("|", "").Trim().Length == 0))
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            parts.Add(key?.GetValue("MachineGuid")?.ToString() ?? Environment.MachineName);
        }
        return string.Join("#", parts).ToUpperInvariant();
    }

    // «RSTK-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX»: 120 бит хеша отпечатка в Base32 (без путающихся букв I, L, O, U).
    public static string MachineKey()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{Product}|{MachineFingerprint()}"));
        return Format(Base32.Encode(hash.AsSpan(0, 15)), 4, "RSTK-");
    }

    public static string Normalize(string text)
    {
        var c = Base32.Clean(text);
        return c.Length == 28 && c.StartsWith("RSTK", StringComparison.Ordinal) ? c[4..] : c;
    }

    public static bool IsKeyWellFormed(string key) => Normalize(key).Length == 24;

    private static string Format(string s, int group, string prefix = "") =>
        prefix + string.Join("-", Enumerable.Range(0, (s.Length + group - 1) / group).Select(i => s.Substring(i * group, Math.Min(group, s.Length - i * group))));

    private static byte[] Payload(string machineKey) => Encoding.UTF8.GetBytes($"{Product}|{Normalize(machineKey)}");

    // ── Серийный номер ──────────────────────────────────
    public static string Sign(string machineKey, string privateKeyPem)
    {
        if (!IsKeyWellFormed(machineKey)) throw new FormatException("Ключ компьютера указан неверно: проверьте, что он скопирован полностью.");
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        var sig = ecdsa.SignData(Payload(machineKey), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Format(Base32.Encode(sig), 5);
    }

    public static bool Verify(string machineKey, string serial)
    {
        try
        {
            var sig = Base32.Decode(Base32.Clean(serial));
            if (sig.Length != 64) return false;
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(PublicKeyPem);
            return ecdsa.VerifyData(Payload(machineKey), sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception) { return false; }
    }

    // Подходит ли секретный ключ к открытому ключу, встроенному в программу.
    public static bool MatchesPublicKey(string privateKeyPem)
    {
        try
        {
            var probe = Format(Base32.Encode(new byte[15]), 4, "RSTK-");
            return Verify(probe, Sign(probe, privateKeyPem));
        }
        catch (Exception) { return false; }
    }

    // ── Файл лицензии ───────────────────────────────────
    public sealed record LicenseFile(string MachineKey, string Serial, DateTime ActivatedAt);

    public static string MachineFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Rostok", "license.json");
    public static string UserFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rostok", "license.json");

    public static void Save(string machineKey, string serial, bool machineWide = true)
    {
        var json = JsonSerializer.Serialize(new LicenseFile(machineKey, serial, DateTime.Now), new JsonSerializerOptions { WriteIndented = true });
        foreach (var path in machineWide ? new[] { MachineFile, UserFile } : [UserFile])
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
                return;
            }
            catch (Exception) when (path != UserFile) { /* нет прав на общую папку — сохраняем для пользователя */ }
        }
    }

    // Программа активирована на этом компьютере: есть файл лицензии, ключ совпадает, подпись верна.
    public static bool IsActivated()
    {
        var key = MachineKey();
        foreach (var path in new[] { MachineFile, UserFile })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var lic = JsonSerializer.Deserialize<LicenseFile>(File.ReadAllText(path));
                if (lic is not null && Normalize(lic.MachineKey) == Normalize(key) && Verify(key, lic.Serial)) return true;
            }
            catch (Exception) { /* повреждённый файл — как будто его нет */ }
        }
        return false;
    }

    // Ранее сохранённая лицензия этого компьютера (ключ совпадает, номер верный) — для повторной установки и обновления.
    public static LicenseFile? Saved(string machineKey)
    {
        foreach (var path in new[] { MachineFile, UserFile })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var lic = JsonSerializer.Deserialize<LicenseFile>(File.ReadAllText(path));
                if (lic is not null && Normalize(lic.MachineKey) == Normalize(machineKey) && Verify(machineKey, lic.Serial)) return lic;
            }
            catch (Exception) { /* повреждённый файл — как будто его нет */ }
        }
        return null;
    }

    // Ключ компьютера запоминается сразу после генерации: пока сотрудник ждёт номер от администратора,
    // установщик можно закрыть — при следующем запуске ключ уже будет показан.
    public static string RequestMachineFile => Path.Combine(Path.GetDirectoryName(MachineFile)!, "machine-key.txt");
    public static string RequestUserFile => Path.Combine(Path.GetDirectoryName(UserFile)!, "machine-key.txt");

    public static void SaveRequest(string machineKey)
    {
        foreach (var path in new[] { RequestMachineFile, RequestUserFile })
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, machineKey);
                return;
            }
            catch (Exception) when (path != RequestUserFile) { /* нет прав на общую папку — сохраняем для пользователя */ }
            catch (Exception) { /* не удалось запомнить — ключ всегда можно сгенерировать заново, он тот же */ }
        }
    }

    public static bool HasRequest() => File.Exists(RequestMachineFile) || File.Exists(RequestUserFile) || File.Exists(MachineFile) || File.Exists(UserFile);
}

// Base32 Крокфорда: цифры и буквы без I, L, O, U — ключ легко продиктовать и переписать.
public static class Base32
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Decode(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var ch in text)
        {
            var v = Alphabet.IndexOf(ch);
            if (v < 0) throw new FormatException();
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. bytes];
    }

    // Лишние символы убираются, похожие буквы заменяются: O → 0, I и L → 1.
    public static string Clean(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.ToUpperInvariant())
        {
            var ch = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };
            if (Alphabet.Contains(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }
}
