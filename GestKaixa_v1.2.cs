using System;
using System.Text;
using MySql.Data.MySqlClient;



namespace GestKaixa
{
    // --------------------------------------------------------
    //  CONSTANTS DE ROLS
    // ---------------------------------------------------
    // -----
    static class Rols
    {
        public const string Admin  = "admin";
        public const string Client = "client";
    }

    // --------------------------------------------------------
    //  CAPA D'ACCÉS A DADES
    // --------------------------------------------------------
    static class DB
    {
        const string ConnStr =
            "Server=127.0.0.1;Database=kaixa;Uid=cashbox_app;Pwd=app123;CharSet=utf8;";

        public static MySqlConnection Connect()
        {
            var conn = new MySqlConnection(ConnStr);
            conn.Open();
            return conn;
        }
    }

    // --------------------------------------------------------
    //  MODEL: usuari de sessió
    // --------------------------------------------------------
    class UsuariSessio
    {
        public int    Id       { get; set; }
        public string Username { get; set; } = "";
        public string Rol      { get; set; } = "";
    }

    // --------------------------------------------------------
    //  AUTENTICACIÓ
    // --------------------------------------------------------
    static class Auth
    {
        public static UsuariSessio? Login(string username, string password)
        {
            // — Accés administrador —
            if (username == "cashbox_app" && password == "app123")
                return new UsuariSessio { Id = 0, Username = "cashbox_app", Rol = Rols.Admin };

            // — Accés client (taula Usuaris) —
            using var conn = DB.Connect();
            const string sql = @"
                SELECT id, username
                FROM   Usuaris
                WHERE  username = @u
                  AND  password = @p";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@p", password);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            return new UsuariSessio
            {
                Id       = rd.GetInt32("id"),
                Username = rd.GetString("username"),
                Rol      = Rols.Client
            };
        }
    }

    // --------------------------------------------------------
    //  OPERACIONS CLIENT
    // --------------------------------------------------------
    static class ClientOps
    {
        public static void VeureComptes(int usuariId)
        {
            UI.Titol("Els teus comptes");
            ImprimirComptes(usuariId);
        }

        static void ImprimirComptes(int usuariId)
        {
            using var conn = DB.Connect();
            const string sql = @"
                SELECT c.id, c.numero_compte, c.estat, vs.saldo
                FROM   Comptes c
                JOIN   UsuarisComptes uc ON uc.compte_id = c.id
                LEFT JOIN VistaSaldos vs ON vs.compte_id = c.id
                WHERE  uc.usuari_id = @uid";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@uid", usuariId);
            using var rd = cmd.ExecuteReader();

            Console.WriteLine($"  {"ID",-5} {"Número",-26} {"Estat",-12} {"Saldo",12}");
            Console.WriteLine("  " + new string('─', 58));
            bool cap = true;
            while (rd.Read())
            {
                cap = false;
                decimal saldo = rd.IsDBNull(rd.GetOrdinal("saldo")) ? 0 : rd.GetDecimal("saldo");
                Console.WriteLine(
                    $"  {rd["id"],-5} {rd["numero_compte"],-26} {rd["estat"],-12} {saldo,11:F2} €");
            }
            if (cap) Console.WriteLine("  (cap compte assignat)");
        }

        public static void ConsultarSaldo(int usuariId)
        {
            UI.Titol("Consultar saldo");
            int compteId = SeleccionarCompte(usuariId);
            if (compteId < 0) return;

            using var conn = DB.Connect();
            const string sql = "SELECT saldo FROM VistaSaldos WHERE compte_id = @cid";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@cid", compteId);
            var saldo = cmd.ExecuteScalar();
            decimal valor = (saldo == null || saldo == DBNull.Value) ? 0 : Convert.ToDecimal(saldo);
            Console.WriteLine($"\n  Saldo actual: {valor:F2} €");
        }

        public static void VeureMoviments(int usuariId)
        {
            UI.Titol("Moviments");
            int compteId = SeleccionarCompte(usuariId);
            if (compteId < 0) return;

            using var conn = DB.Connect();
            const string sql = @"
                SELECT data, import, saldo, concepte
                FROM   Moviments
                WHERE  compte_id = @cid
                ORDER  BY data DESC
                LIMIT  20";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@cid", compteId);
            using var rd = cmd.ExecuteReader();

            Console.WriteLine();
            Console.WriteLine($"  {"Data",-22} {"Import",10}  {"Saldo",10}  Concepte");
            Console.WriteLine("  " + new string('─', 66));
            bool cap = true;
            while (rd.Read())
            {
                cap = false;
                decimal import = rd.IsDBNull(rd.GetOrdinal("import")) ? 0 : rd.GetDecimal("import");
                decimal saldo  = rd.IsDBNull(rd.GetOrdinal("saldo"))  ? 0 : rd.GetDecimal("saldo");
                string color   = import >= 0 ? "\x1b[32m" : "\x1b[31m";
                Console.WriteLine(
                    $"  {rd["data"],-22} {color}{import,9:F2} €\x1b[0m  {saldo,9:F2} €  {rd["concepte"]}");
            }
            if (cap) Console.WriteLine("  (sense moviments)");
        }

        public static void FerIngres(int usuariId)
        {
            UI.Titol("Fer un ingrés");
            int compteId = SeleccionarCompte(usuariId);
            if (compteId < 0) return;

            decimal import = UI.DemanarImport("Import de l'ingrés");
            if (import <= 0) return;
            string concepte = UI.Llegir("Concepte");

            // ── Millora usabilitat v1.2: confirmació abans de registrar ──
            if (!UI.Confirmar($"Confirmes l'ingrés de {import:F2} € ?")) return;

            RegistrarMoviment(compteId, import, concepte);
        }

        public static void FerRetirada(int usuariId)
        {
            UI.Titol("Fer una retirada");
            int compteId = SeleccionarCompte(usuariId);
            if (compteId < 0) return;

            decimal import = UI.DemanarImport("Import a retirar");
            if (import <= 0) return;

            // ── Comprovació de saldo suficient ────────────────────────────
            using var connCheck = DB.Connect();
            const string sqlSaldo = "SELECT saldo FROM VistaSaldos WHERE compte_id = @cid";
            using var cmdCheck = new MySqlCommand(sqlSaldo, connCheck);
            cmdCheck.Parameters.AddWithValue("@cid", compteId);
            var saldoObj = cmdCheck.ExecuteScalar();
            decimal saldoActual = (saldoObj == null || saldoObj == DBNull.Value)
                                  ? 0 : Convert.ToDecimal(saldoObj);

            if (import > saldoActual)
            {
                UI.Error($"Saldo insuficient. Saldo disponible: {saldoActual:F2} €");
                return;
            }

            string concepte = UI.Llegir("Concepte");

            // ── Millora usabilitat v1.2: confirmació abans de registrar ──
            if (!UI.Confirmar($"Confirmes la retirada de {import:F2} € ?")) return;

            RegistrarMoviment(compteId, -import, concepte);
        }

        public static void VeureAlertes(int usuariId)
        {
            UI.Titol("Alertes");
            using var conn = DB.Connect();
            const string sql = @"
                SELECT a.data, a.missatge
                FROM   Alertes a
                JOIN   UsuarisComptes uc ON uc.compte_id = a.compte_id
                WHERE  uc.usuari_id = @uid
                ORDER  BY a.data DESC
                LIMIT  15";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@uid", usuariId);
            using var rd = cmd.ExecuteReader();

            bool cap = true;
            while (rd.Read())
            {
                cap = false;
                Console.WriteLine($"  \x1b[33m[!] {rd["data"]}  {rd["missatge"]}\x1b[0m");
            }
            if (cap) Console.WriteLine("  (sense alertes)");
        }

        static int SeleccionarCompte(int usuariId)
        {
            ImprimirComptes(usuariId);
            Console.Write("\n  ID del compte: ");
            if (!int.TryParse(Console.ReadLine(), out int id)) return -1;

            using var conn = DB.Connect();
            const string sql =
                "SELECT COUNT(*) FROM UsuarisComptes WHERE usuari_id=@u AND compte_id=@c";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@u", usuariId);
            cmd.Parameters.AddWithValue("@c", id);
            long ok = (long)(cmd.ExecuteScalar() ?? 0L);
            if (ok == 0) { UI.Error("Compte no vàlid."); return -1; }
            return id;
        }

        static void RegistrarMoviment(int compteId, decimal import, string concepte)
        {
            try
            {
                using var conn = DB.Connect();
                const string sql = @"
                    INSERT INTO Moviments (compte_id, import, concepte, data)
                    VALUES (@cid, @import, @concepte, NOW())";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@cid",      compteId);
                cmd.Parameters.AddWithValue("@import",   import);
                cmd.Parameters.AddWithValue("@concepte", concepte);
                cmd.ExecuteNonQuery();
                UI.Ok("Moviment registrat correctament.");
            }
            catch (MySqlException ex)
            {
                UI.Error("Operació rebutjada: " + ex.Message);
            }
        }
    }

    // --------------------------------------------------------
    //  OPERACIONS ADMINISTRADOR  (NOU a v1.2)
    // --------------------------------------------------------
    static class AdminOps
    {
        public static void RegistrarUsuari()
        {
            UI.Titol("Registrar nou usuari");
            string dni      = UI.Llegir("DNI (9 caràcters)");
            string nom      = UI.Llegir("Nom");
            string cognom   = UI.Llegir("Cognom");
            string adreca   = UI.Llegir("Adreça (opcional, Intro per saltar)");
            string telefon  = UI.Llegir("Telèfon (opcional, Intro per saltar)");
            string username = UI.Llegir("Nom d'usuari");
            string pass     = UI.LlegirPassword("Contrasenya");

            // ── Millora usabilitat v1.2: confirmació ─────────
            if (!UI.Confirmar($"Crear usuari '{username}' ?")) return;

            try
            {
                using var conn = DB.Connect();
                const string sql = @"
                    INSERT INTO Usuaris (dni, nom, cognom, adreca, telefon, username, password)
                    VALUES (@dni, @nom, @cognom, @adreca, @telefon, @username, @pass)";
                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@dni",      dni);
                cmd.Parameters.AddWithValue("@nom",      nom);
                cmd.Parameters.AddWithValue("@cognom",   cognom);
                cmd.Parameters.AddWithValue("@adreca",   string.IsNullOrEmpty(adreca)  ? (object)DBNull.Value : adreca);
                cmd.Parameters.AddWithValue("@telefon",  string.IsNullOrEmpty(telefon) ? (object)DBNull.Value : telefon);
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@pass",     pass);
                cmd.ExecuteNonQuery();
                UI.Ok($"Usuari '{username}' creat correctament.");
            }
            catch (MySqlException ex) { UI.Error("Error: " + ex.Message); }
        }

        public static void ObrirCompte()
        {
            UI.Titol("Obrir nou compte");
            string numero = UI.Llegir("Número de compte (IBAN o codi intern)");

            // ── Millora usabilitat v1.2: confirmació ─────────
            if (!UI.Confirmar($"Crear el compte '{numero}' ?")) return;

            try
            {
                using var conn = DB.Connect();
                const string sql = "INSERT INTO Comptes (numero_compte) VALUES (@num)";
                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@num", numero);
                cmd.ExecuteNonQuery();
                UI.Ok($"Compte creat amb ID {cmd.LastInsertedId}.");
            }
            catch (MySqlException ex) { UI.Error("Error: " + ex.Message); }
        }

        public static void AssignarUsuariCompte()
        {
            UI.Titol("Assignar usuari a compte");
            Console.Write("  ID de l'usuari: ");
            if (!int.TryParse(Console.ReadLine(), out int uId)) { UI.Error("ID no vàlid."); return; }
            Console.Write("  ID del compte:  ");
            if (!int.TryParse(Console.ReadLine(), out int cId)) { UI.Error("ID no vàlid."); return; }

            Console.WriteLine("  Rol: 1) TITULAR  2) AUTORITZAT");
            Console.Write("  Opció: ");
            string rol = (Console.ReadLine()?.Trim() == "2") ? "AUTORITZAT" : "TITULAR";

            // ── Millora usabilitat v1.2: confirmació ─────────
            if (!UI.Confirmar($"Assignar usuari {uId} al compte {cId} com a {rol} ?")) return;

            try
            {
                using var conn = DB.Connect();
                const string sql = @"
                    INSERT IGNORE INTO UsuarisComptes (usuari_id, compte_id, rol)
                    VALUES (@uid, @cid, @rol)";
                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@uid", uId);
                cmd.Parameters.AddWithValue("@cid", cId);
                cmd.Parameters.AddWithValue("@rol", rol);
                cmd.ExecuteNonQuery();
                UI.Ok($"Usuari {uId} assignat al compte {cId} com a {rol}.");
            }
            catch (MySqlException ex) { UI.Error("Error: " + ex.Message); }
        }

        // ── NOU v1.2: Llistat complet d'usuaris i comptes ────
        public static void LlistarUsuarisComptes()
        {
            UI.Titol("Tots els usuaris i comptes");
            using var conn = DB.Connect();
            const string sql = @"
                SELECT u.id AS uid, u.username, u.nom, u.cognom,
                       c.id AS cid, c.numero_compte, c.estat,
                       uc.rol AS rol_compte, vs.saldo
                FROM   Usuaris u
                LEFT JOIN UsuarisComptes uc ON uc.usuari_id = u.id
                LEFT JOIN Comptes c         ON c.id = uc.compte_id
                LEFT JOIN VistaSaldos vs    ON vs.compte_id = c.id
                ORDER  BY u.id, c.id";

            using var cmd = new MySqlCommand(sql, conn);
            using var rd  = cmd.ExecuteReader();

            int lastUid = -1;
            int totalUsuaris = 0;
            Console.WriteLine();
            while (rd.Read())
            {
                int uid = rd.GetInt32("uid");
                if (uid != lastUid)
                {
                    lastUid = uid;
                    totalUsuaris++;
                    Console.WriteLine(
                        $"  \x1b[36m▸ [{uid}] {rd["username"]}  —  {rd["nom"]} {rd["cognom"]}\x1b[0m");
                }
                if (!rd.IsDBNull(rd.GetOrdinal("cid")))
                {
                    decimal saldo = rd.IsDBNull(rd.GetOrdinal("saldo")) ? 0 : rd.GetDecimal("saldo");
                    Console.WriteLine(
                        $"       └─ [{rd["cid"]}] {rd["numero_compte"],-24} " +
                        $"{rd["estat"],-10} {rd["rol_compte"],-12} {saldo,10:F2} €");
                }
                else
                {
                    Console.WriteLine("       └─ (sense comptes)");
                }
            }

            // ── Millora usabilitat v1.2: resum al final ───────
            Console.WriteLine();
            Console.WriteLine($"  \x1b[90mTotal usuaris: {totalUsuaris}\x1b[0m");
        }
    }

    // --------------------------------------------------------
    //  INTERFÍCIE D'USUARI
    // --------------------------------------------------------
    static class UI
    {
        public static void Titol(string text)
        {
            Console.WriteLine();
            Console.WriteLine($"  \x1b[1m\x1b[36m══ {text} ══\x1b[0m");
            Console.WriteLine();
        }

        public static void Ok(string text)    => Console.WriteLine($"\n  \x1b[32m✓ {text}\x1b[0m");
        public static void Error(string text) => Console.WriteLine($"\n  \x1b[31m✗ {text}\x1b[0m");

        public static string Llegir(string prompt)
        {
            Console.Write($"  {prompt}: ");
            return Console.ReadLine()?.Trim() ?? "";
        }

        // ── Ocultació del password amb asteriscs (v1.2) ───────
        public static string LlegirPassword(string prompt)
        {
            Console.Write($"  {prompt}: ");
            var sb = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0) { sb.Remove(sb.Length - 1, 1); Console.Write("\b \b"); }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                    Console.Write('*');
                }
            }
            Console.WriteLine();
            return sb.ToString();
        }

        public static decimal DemanarImport(string missatge)
        {
            Console.Write($"  {missatge}: ");
            if (!decimal.TryParse(Console.ReadLine()?.Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal val) || val <= 0)
            {
                Error("Import no vàlid.");
                return -1;
            }
            return val;
        }

        // ── NOU v1.2: confirmació S/N ──────────────────────────
        public static bool Confirmar(string pregunta)
        {
            Console.Write($"\n  {pregunta} [S/N]: ");
            string resp = Console.ReadLine()?.Trim().ToUpper() ?? "";
            if (resp == "S") return true;
            Console.WriteLine("  Operació cancel·lada.");
            return false;
        }

        public static void Separador() => Console.WriteLine("  " + new string('─', 40));

        public static void Pausa()
        {
            Console.WriteLine("\n  Prem Intro per continuar...");
            Console.ReadLine();
        }
    }

    // --------------------------------------------------------
    //  MENÚS
    // --------------------------------------------------------
    static class Menus
    {
        public static void MenuClient(UsuariSessio sessio)
        {
            bool actiu = true;
            while (actiu)
            {
                Console.Clear();
                Console.WriteLine($"\n  \x1b[1mGestKaixa v1.2 — {sessio.Username}\x1b[0m");
                UI.Separador();
                Console.WriteLine("  1. Veure els meus comptes");
                Console.WriteLine("  2. Consultar saldo");
                Console.WriteLine("  3. Veure moviments");
                Console.WriteLine("  4. Fer un ingrés");
                Console.WriteLine("  5. Fer una retirada");
                Console.WriteLine("  6. Veure alertes");
                Console.WriteLine("  0. Sortir");
                UI.Separador();
                Console.Write("  Opció: ");

                switch (Console.ReadLine()?.Trim())
                {
                    case "1": ClientOps.VeureComptes(sessio.Id);   UI.Pausa(); break;
                    case "2": ClientOps.ConsultarSaldo(sessio.Id); UI.Pausa(); break;
                    case "3": ClientOps.VeureMoviments(sessio.Id); UI.Pausa(); break;
                    case "4": ClientOps.FerIngres(sessio.Id);      UI.Pausa(); break;
                    case "5": ClientOps.FerRetirada(sessio.Id);    UI.Pausa(); break;
                    case "6": ClientOps.VeureAlertes(sessio.Id);   UI.Pausa(); break;
                    case "0": actiu = false;                                    break;
                    default:  UI.Error("Opció no vàlida.");        UI.Pausa(); break;
                }
            }
        }

        // ── NOU v1.2: Menú administrador activat ─────────────
        public static void MenuAdmin(UsuariSessio sessio)
        {
            bool actiu = true;
            while (actiu)
            {
                Console.Clear();
                Console.WriteLine($"\n  \x1b[1mGestKaixa v1.2 — Administrador ({sessio.Username})\x1b[0m");
                UI.Separador();
                Console.WriteLine("  1. Registrar nou usuari");
                Console.WriteLine("  2. Obrir nou compte");
                Console.WriteLine("  3. Assignar usuari a compte");
                Console.WriteLine("  4. Llistar tots els usuaris i comptes");
                Console.WriteLine("  0. Sortir");
                UI.Separador();
                Console.Write("  Opció: ");

                switch (Console.ReadLine()?.Trim())
                {
                    case "1": AdminOps.RegistrarUsuari();        UI.Pausa(); break;
                    case "2": AdminOps.ObrirCompte();            UI.Pausa(); break;
                    case "3": AdminOps.AssignarUsuariCompte();   UI.Pausa(); break;
                    case "4": AdminOps.LlistarUsuarisComptes();  UI.Pausa(); break;
                    case "0": actiu = false;                                  break;
                    default:  UI.Error("Opció no vàlida.");      UI.Pausa(); break;
                }
            }
        }
    }

    // --------------------------------------------------------
    //  PUNT D'ENTRADA
    // --------------------------------------------------------
    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Clear();

            Console.WriteLine("\n  \x1b[1m\x1b[36m╔══════════════════════════╗");
            Console.WriteLine("  ║    GestKaixa  v1.2       ║");
            Console.WriteLine("  ╚══════════════════════════╝\x1b[0m\n");

            try { using var _ = DB.Connect(); }
            catch (Exception ex)
            {
                Console.WriteLine("\n  \x1b[31m✗ No s'ha pogut connectar a la base de dades.\x1b[0m");
                Console.WriteLine("  " + ex.Message);
                return;
            }

            UsuariSessio? sessio = null;
            for (int intent = 0; intent < 3; intent++)
            {
                string nom  = UI.Llegir("Usuari");
                string pass = UI.LlegirPassword("Password");   // ← asteriscs
                sessio = Auth.Login(nom, pass);
                if (sessio != null) break;
                UI.Error("Credencials incorrectes.");
                if (intent < 2) Console.WriteLine("  Torna-ho a intentar.\n");
            }

            if (sessio == null)
            {
                UI.Error("Massa intents fallits. Accés denegat.");
                return;
            }

            UI.Ok($"Benvingut/da, {sessio.Username}!");
            System.Threading.Thread.Sleep(900);

            // ── v1.2: menú admin descomentado y activo ────────
            if (sessio.Rol == Rols.Admin)
                Menus.MenuAdmin(sessio);
            else
                Menus.MenuClient(sessio);

            Console.WriteLine("\n  Fins aviat!\n");
        }
    }
}