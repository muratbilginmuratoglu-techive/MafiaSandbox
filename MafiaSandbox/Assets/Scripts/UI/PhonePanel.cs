using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Factions;
using MafiaSandbox.Player;
using MafiaSandbox.World;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// The mobile phone (T): a small IMGUI phone with Messages (threads, quick replies, free text), Contacts (crews, shops, the fixer, mum, the
    /// police; call or text them) and Settings (edit character). Game events drop texts into the inbox: a welcome message, a crew warning
    /// when you walk onto their turf for the first time, and the fixer's heads-up when the heat rises.
    /// </summary>
    public class PhonePanel : MonoBehaviour
    {
        public static PhonePanel I { get; private set; }
        public static bool IsOpen => I && I.open;
        bool open;

        enum Page { Home, Messages, Thread, Contacts, Contact, Settings, Missions, Jobs }
        Page page = Page.Home; Conversation thread; Contact contact; Vector2 scrollList, scrollThread; string draft = ""; bool typing;

        class Msg { public bool mine; public string text, time; }
        class Contact { public string name, role, number, kind, id; public Color color; public FactionDef faction; public Shop shop; }
        class Conversation { public Contact c; public List<Msg> msgs = new List<Msg>(); public int unread; }
        readonly List<Contact> contacts = new List<Contact>();
        readonly List<Conversation> convs = new List<Conversation>();
        readonly List<(float t, Conversation c, string text)> pending = new List<(float, Conversation, string)>();
        readonly HashSet<string> warned = new HashSet<string>();
        Contact sal, mom, police; bool welcomed, built; float welcomeT = -1f; float lastHeatT = -99f; int lastWanted; FactionDef lastTerr; float notifyT; string notifyText;
        GUIStyle bubble, bubbleText, field; PlayerController pc;

        void Awake() { I = this; }
        public static void Post(string id, string text) { if (!I) return; I.BuildContacts(); foreach (var c in I.contacts) if (c.id == id) { I.Notify(c, text); return; } }

        int Unread { get { int n = 0; foreach (var c in convs) n += c.unread; return n; } }
        static string Clock() { var d = FindAnyObjectByType<DayNightCycle>(); float h = d ? d.timeOfDay : 12f; int hh = Mathf.FloorToInt(h) % 24, mm = Mathf.FloorToInt((h - Mathf.Floor(h)) * 60f); return $"{hh:00}:{mm:00}"; }

        void BuildContacts()
        {
            if (built) return; built = true;
            string Num(string s) { int h = 0; foreach (var ch in s) h = h * 31 + ch; return "555-" + (Mathf.Abs(h) % 9000 + 1000); }
            Contact Add(string id, string name, string role, string kind, Color col, FactionDef f = null, Shop sh = null) { var c = new Contact { id = id, name = name, role = role, kind = kind, color = col, number = Num(id), faction = f, shop = sh }; contacts.Add(c); return c; }
            mom = Add("jeremy", "Jeremy", "Old friend", "mom", new Color(0.55f, 0.75f, 0.95f));
            sal = Add("sal", "Sal \"The Fixer\"", "Knows people", "fixer", UiSkin.Accent);
            police = Add("police", "Police Station", "Emergency", "police", UiSkin.Blue);
            foreach (var f in FactionDefs.All) Add(f.id, f.name, f.kind == FactionKind.Mafia ? "Family" : "Gang", "crew", f.color, f);
            var eco = EconomyManager.I;
            if (eco) foreach (var sh in eco.shops) { var bi = sh.info; if (!bi || bi.kind == "house" || bi.kind == "home" || bi.kind == "police") continue; Add("shop_" + bi.label, string.IsNullOrEmpty(bi.ownerName) ? bi.label : bi.ownerName, bi.label, "shop", new Color(0.55f, 0.8f, 0.6f), null, sh); }
        }

        Conversation Conv(Contact c) { foreach (var v in convs) if (v.c == c) return v; var n = new Conversation { c = c }; convs.Add(n); return n; }

        void Notify(Contact from, string text, bool silent = false)
        {
            var cv = Conv(from); cv.msgs.Add(new Msg { mine = false, text = text, time = Clock() });
            if (!(open && page == Page.Thread && thread == cv)) cv.unread++;
            if (!silent) { notifyText = $"{from.name}: {text}"; notifyT = 4.5f; GameManager.I?.Say($"New message from {from.name}"); }
            convs.Remove(cv); convs.Insert(0, cv);
        }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player || CharacterCreator.IsOpen || IntroCinematic.Playing) return; pc = gm.player;
            BuildContacts();
            string me = PlayerAppearance.PlayerName;
            if (welcomeT < 0f) welcomeT = Time.time;
            if (!welcomed && Time.time - welcomeT > 3f) { welcomed = true; Notify(sal, $"Welcome to town, {me}. Three families and four crews split this place. Keep your head down, keep some cash on you. Text me if the law gets interested."); }
            // events → texts
            var fm = FactionManager.I;
            if (fm && fm.Territory != lastTerr) { lastTerr = fm.Territory; if (lastTerr != null && warned.Add("t_" + lastTerr.id)) { foreach (var c in contacts) if (c.faction == lastTerr) Notify(c, $"{me}. You're on {lastTerr.name} ground. Watch your step."); } }
            if (WantedSystem.I) { int lv = WantedSystem.I.Level; if (lv > lastWanted && Time.time - lastHeatT > 25f) { lastHeatT = Time.time; Notify(sal, lv >= 3 ? "Every cop in town is looking for you. Get off the street NOW." : lv == 2 ? "Heat's rising. Ditch the car, get out of sight." : "Somebody called it in. Keep walking, don't run."); } lastWanted = lv; }
            for (int i = pending.Count - 1; i >= 0; i--) if (Time.time >= pending[i].t) { var p = pending[i]; pending.RemoveAt(i); Notify(p.c.c, p.text); }
            if (notifyT > 0f) notifyT -= Time.unscaledDeltaTime;

            bool dead = pc.GetComponent<MafiaSandbox.Combat.CharacterBody>()?.Dead ?? false;
            if (Input.GetKeyDown(KeyCode.T) && !open && !dead && !GameUI.Blocking && !gm.Arrested) { open = true; page = Page.Home; }
            else if (open && ((Input.GetKeyDown(KeyCode.T) && !typing) || Input.GetKeyDown(KeyCode.Escape) || dead)) { open = false; }
        }

        void Close() { open = false; }

        // ------------------------------------------------------------------ drawing
        void Styles()
        {
            if (bubble != null) return;
            bubble = new GUIStyle { border = new RectOffset(4, 4, 4, 4), padding = new RectOffset(10, 10, 6, 6), wordWrap = true, fontSize = 13, richText = true, alignment = TextAnchor.MiddleLeft };
            bubble.normal.background = UiSkin.BarFillTex(); bubble.normal.textColor = Color.white;
            field = new GUIStyle(GUI.skin.textField) { fontSize = 14, padding = new RectOffset(8, 8, 5, 5) }; field.normal.textColor = Color.white; field.focused.textColor = Color.white;
        }

        void OnGUI()
        {
            if (CharacterCreator.IsOpen || IntroCinematic.Playing) return;
            float W = Screen.width, H = Screen.height;
            if (!open)
            {
                if (GameUI.Blocking) return;
                int u = Unread; if (u > 0) UiSkin.DrawChip(new Rect(W - 190, 12, 178, 24), $"PHONE  T   ·   {u} new", UiSkin.Accent);
                if (notifyT > 0f && !string.IsNullOrEmpty(notifyText)) { var nr = new Rect(W - 330, 44, 318, 48); UiSkin.DrawPanel(nr); GUI.Label(new Rect(nr.x + 10, nr.y + 6, nr.width - 20, nr.height - 12), "<b>New message</b>\n" + (notifyText.Length > 70 ? notifyText.Substring(0, 70) + "…" : notifyText), UiSkin.Label); }
                return;
            }
            Styles();
            float pw = 330, ph = Mathf.Min(620, H - 40); var r = new Rect(W - pw - 40, (H - ph) / 2f, pw, ph);
            UiSkin.DrawPanel(r, true);
            var inner = new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24);
            GUILayout.BeginArea(inner);
            // status bar
            GUILayout.BeginHorizontal(); GUILayout.Label(Clock(), UiSkin.LabelBold); GUILayout.FlexibleSpace(); GUILayout.Label("LTE  100%", UiSkin.LabelDim); GUILayout.EndHorizontal();
            GUILayout.Space(4);
            typing = false;
            switch (page)
            {
                case Page.Home: Home(inner); break;
                case Page.Messages: Messages(inner); break;
                case Page.Thread: Thread(inner); break;
                case Page.Contacts: Contacts(inner); break;
                case Page.Contact: ContactPage(inner); break;
                case Page.Settings: Settings(inner); break;
                case Page.Missions: MissionsPage(inner); break;
                case Page.Jobs: JobsPage(inner); break;
            }
            GUILayout.EndArea();
            // home button
            var hb = new Rect(r.center.x - 32, r.yMax - 14, 64, 5); GUI.color = new Color(1f, 1f, 1f, 0.55f); GUI.DrawTexture(hb, UiSkin.White); GUI.color = Color.white;
            if (GUI.Button(new Rect(hb.x - 20, hb.y - 8, hb.width + 40, 20), GUIContent.none, GUIStyle.none)) { if (page == Page.Home) Close(); else page = Page.Home; }
        }

        void Home(Rect inner)
        {
            GUILayout.Space(10);
            GUILayout.Label(Clock(), new GUIStyle(UiSkin.Big) { fontSize = 44 });
            GUILayout.Label($"<b>{PlayerAppearance.PlayerName}</b>   ·   ${EconomyManager.I?.cash ?? 0}", new GUIStyle(UiSkin.Center) { fontSize = 14 });
            GUILayout.Space(30);
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            if (Icon("Messages", new Color(0.3f, 0.78f, 0.4f), "MSG", Unread)) page = Page.Messages;
            if (Icon("Contacts", new Color(0.3f, 0.55f, 0.95f), "CT", 0)) page = Page.Contacts;
            if (Icon("Missions", new Color(0.95f, 0.7f, 0.2f), "MIS", 0)) page = Page.Missions;
            if (Icon("Jobs", new Color(0.85f, 0.35f, 0.35f), "JOB", 0)) page = Page.Jobs;
            if (Icon("Settings", new Color(0.55f, 0.58f, 0.66f), "SET", 0)) page = Page.Settings;
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Esc / T closes", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
        }

        bool Icon(string label, Color c, string glyph, int badge)
        {
            GUILayout.BeginVertical(GUILayout.Width(86));
            var rc = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
            GUI.backgroundColor = c; GUI.Box(rc, GUIContent.none, bubble); GUI.backgroundColor = Color.white;
            GUI.Label(rc, glyph, UiSkin.Big);
            if (badge > 0) { var br = new Rect(rc.xMax - 16, rc.y - 6, 22, 20); GUI.backgroundColor = UiSkin.Red; GUI.Box(br, GUIContent.none, bubble); GUI.backgroundColor = Color.white; GUI.Label(br, badge.ToString(), new GUIStyle(UiSkin.Center) { fontSize = 12, fontStyle = FontStyle.Bold }); }
            bool hit = GUI.Button(rc, GUIContent.none, GUIStyle.none);
            GUILayout.Label(label, new GUIStyle(UiSkin.Center) { fontSize = 12 });
            GUILayout.EndVertical(); return hit;
        }

        void Header(string title, Page back)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", UiSkin.Button, GUILayout.Width(34), GUILayout.Height(28))) page = back;
            GUILayout.Label(title, UiSkin.Title); GUILayout.EndHorizontal(); GUILayout.Space(6);
        }

        void Messages(Rect inner)
        {
            Header("Messages", Page.Home);
            scrollList = GUILayout.BeginScrollView(scrollList);
            if (convs.Count == 0) GUILayout.Label("No messages yet. Open Contacts to text someone.", UiSkin.LabelDim);
            foreach (var cv in convs)
            {
                var last = cv.msgs.Count > 0 ? cv.msgs[cv.msgs.Count - 1] : null;
                var rc = GUILayoutUtility.GetRect(10, 54, GUILayout.ExpandWidth(true));
                if (GUI.Button(rc, GUIContent.none, UiSkin.Button)) { thread = cv; cv.unread = 0; page = Page.Thread; scrollThread = new Vector2(0, 99999); }
                GUI.color = cv.c.color; GUI.DrawTexture(new Rect(rc.x + 10, rc.y + 11, 32, 32), UiSkin.White); GUI.color = Color.white;
                GUI.Label(new Rect(rc.x + 10, rc.y + 11, 32, 32), cv.c.name.Substring(0, 1), UiSkin.Big);
                GUI.Label(new Rect(rc.x + 50, rc.y + 5, rc.width - 90, 22), (cv.unread > 0 ? "<b>" + cv.c.name + "</b>" : cv.c.name), UiSkin.Label);
                string pre = last == null ? "" : (last.mine ? "You: " : "") + last.text; if (pre.Length > 34) pre = pre.Substring(0, 34) + "…";
                GUI.Label(new Rect(rc.x + 50, rc.y + 26, rc.width - 60, 22), pre, UiSkin.LabelDim);
                if (cv.unread > 0) { var br = new Rect(rc.xMax - 30, rc.y + 17, 20, 20); GUI.backgroundColor = UiSkin.Red; GUI.Box(br, GUIContent.none, bubble); GUI.backgroundColor = Color.white; GUI.Label(br, cv.unread.ToString(), new GUIStyle(UiSkin.Center) { fontSize = 12 }); }
                GUILayout.Space(2);
            }
            GUILayout.EndScrollView();
        }

        void Thread(Rect inner)
        {
            if (thread == null) { page = Page.Messages; return; }
            Header(thread.c.name, Page.Messages);
            scrollThread = GUILayout.BeginScrollView(scrollThread, GUILayout.Height(inner.height - 150));
            float maxW = inner.width - 70;
            foreach (var m in thread.msgs)
            {
                var content = new GUIContent(m.text); float w = Mathf.Min(maxW, UiSkin.Label.CalcSize(content).x + 26); float h = bubble.CalcHeight(content, w);
                GUILayout.BeginHorizontal(); if (m.mine) GUILayout.FlexibleSpace();
                GUI.backgroundColor = m.mine ? new Color(0.25f, 0.5f, 0.95f) : new Color(0.28f, 0.3f, 0.36f);
                GUILayout.Label(m.text, bubble, GUILayout.Width(w), GUILayout.Height(h)); GUI.backgroundColor = Color.white;
                if (!m.mine) GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.Space(3);
            }
            GUILayout.EndScrollView();
            GUILayout.Space(4);
            // quick replies
            GUILayout.BeginHorizontal();
            foreach (var q in QuickReplies(thread.c)) if (GUILayout.Button(q, UiSkin.Button, GUILayout.Height(26))) Send(thread, q);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("phonemsg");
            draft = GUILayout.TextField(draft, 80, field, GUILayout.Height(30));
            typing = GUI.GetNameOfFocusedControl() == "phonemsg";
            bool enter = typing && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if ((GUILayout.Button("Send", UiSkin.ButtonOn, GUILayout.Width(60), GUILayout.Height(30)) || enter) && draft.Trim().Length > 0) { Send(thread, draft.Trim()); draft = ""; }
            GUILayout.EndHorizontal();
        }

        static string[] QuickReplies(Contact c) => c.kind == "shop" ? new[] { "Are you open?", "Got any deals?" } : c.kind == "crew" ? new[] { "Just passing", "Back off" } : c.kind == "police" ? new[] { "Wrong number", "Any news?" } : c.kind == "fixer" ? new[] { "Any heat?", "Need a favour" } : new[] { "I'm fine", "Thanks, Jeremy" };

        void Send(Conversation cv, string text)
        {
            cv.msgs.Add(new Msg { mine = true, text = text, time = Clock() }); scrollThread = new Vector2(0, 99999);
            pending.Add((Time.time + Random.Range(1.5f, 3.5f), cv, Reply(cv.c, text)));
        }

        string Reply(Contact c, string said)
        {
            string me = PlayerAppearance.PlayerName; int wl = WantedSystem.I ? WantedSystem.I.Level : 0;
            switch (c.kind)
            {
                case "mom": return Pick($"You eating, {me}? Don't forget to eat.", "Three years you were gone. I'm not losing you again.", "Be careful out there. This city eats people.", "Need a place to crash, my couch is yours.");
                case "fixer": return wl > 0 ? $"Heat is {new string('★', wl)} right now. Stay off the main roads and wait it out." : Pick("Quiet at the moment. Enjoy it.", "Nothing on my radar. Check back later.", "I hear the crews are tense lately. Mind your manners.");
                case "police": return wl > 0 ? "We know who this is. Turn yourself in." : Pick("This line is for emergencies only.", "Nothing to report. Don't call here again.", "Station's quiet. Keep it that way.");
                case "crew": { float rep = FactionManager.I ? FactionManager.I.Rep(c.faction) : 0f; string st = FactionManager.Standing(rep); return st == "hostile" ? "You've got nerve texting us. Don't come around." : st == "unfriendly" ? "We're watching you. Keep your distance." : st == "neutral" ? Pick("We don't know you. Don't make us.", "Stay out of our business, we stay out of yours.") : "You're alright with us. Come by anytime."; }
                case "shop": return Pick("We're open. Come by the counter.", "Cash only, no tabs.", "Bring money and we'll talk.", "Today's stock is fresh. Don't wait too long.");
            }
            return "...";
        }

        static string Pick(params string[] s) => s[Random.Range(0, s.Length)];

        void Contacts(Rect inner)
        {
            Header("Contacts", Page.Home);
            scrollList = GUILayout.BeginScrollView(scrollList);
            string[] groups = { "mom", "fixer", "police", "crew", "shop" }; string[] names = { "OLD FRIEND", "FIXER", "EMERGENCY", "CREWS", "SHOPS" };
            for (int g = 0; g < groups.Length; g++)
            {
                bool any = false;
                foreach (var c in contacts)
                {
                    if (c.kind != groups[g]) continue;
                    if (!any) { any = true; GUILayout.Label(names[g], UiSkin.H2); }
                    var rc = GUILayoutUtility.GetRect(10, 40, GUILayout.ExpandWidth(true));
                    if (GUI.Button(rc, GUIContent.none, UiSkin.Button)) { contact = c; page = Page.Contact; }
                    GUI.color = c.color; GUI.DrawTexture(new Rect(rc.x + 8, rc.y + 7, 26, 26), UiSkin.White); GUI.color = Color.white;
                    GUI.Label(new Rect(rc.x + 8, rc.y + 7, 26, 26), c.name.Substring(0, 1), UiSkin.Center);
                    GUI.Label(new Rect(rc.x + 42, rc.y + 2, rc.width - 50, 20), c.name, UiSkin.Label);
                    GUI.Label(new Rect(rc.x + 42, rc.y + 20, rc.width - 50, 18), c.role + "  ·  " + c.number, UiSkin.Small);
                    GUILayout.Space(1);
                }
            }
            GUILayout.EndScrollView();
        }

        void ContactPage(Rect inner)
        {
            if (contact == null) { page = Page.Contacts; return; }
            Header("Contact", Page.Contacts);
            var rc = GUILayoutUtility.GetRect(10, 84, GUILayout.ExpandWidth(true));
            GUI.color = contact.color; GUI.DrawTexture(new Rect(rc.center.x - 30, rc.y, 60, 60), UiSkin.White); GUI.color = Color.white;
            GUI.Label(new Rect(rc.center.x - 30, rc.y, 60, 60), contact.name.Substring(0, 1), UiSkin.Big);
            GUILayout.Label($"<size=20><b>{contact.name}</b></size>", new GUIStyle(UiSkin.Center) { richText = true });
            GUILayout.Label(contact.role, new GUIStyle(UiSkin.LabelDim) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Label(contact.number, new GUIStyle(UiSkin.Center) { fontSize = 16 });
            if (contact.kind == "crew" && FactionManager.I) { float rep = FactionManager.I.Rep(contact.faction); GUILayout.Label($"Standing: <b>{FactionManager.Standing(rep)}</b>  ({rep:0})", new GUIStyle(UiSkin.Center) { richText = true }); }
            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Message", UiSkin.ButtonOn, GUILayout.Height(38))) { thread = Conv(contact); thread.unread = 0; page = Page.Thread; scrollThread = new Vector2(0, 99999); }
            if (GUILayout.Button("Call", UiSkin.Button, GUILayout.Height(38))) { var cv = Conv(contact); cv.msgs.Add(new Msg { mine = true, text = "(called)", time = Clock() }); pending.Add((Time.time + 2.5f, cv, Reply(contact, ""))); GameManager.I?.Say($"Calling {contact.name}…"); Close(); }
            GUILayout.EndHorizontal();
        }

        void JobsPage(Rect inner)
        {
            Header("Jobs from Sal", Page.Home);
            var jm = Gang.JobManager.I; if (!jm) { GUILayout.Label("No jobs.", UiSkin.LabelDim); return; }
            var wrap = new GUIStyle(UiSkin.LabelDim) { wordWrap = true, fontSize = 12 };
            scrollList = GUILayout.BeginScrollView(scrollList);
            GUILayout.Label($"<b>{Gang.Respect.RankName}</b>   ·   debt left ${Gang.CrewManager.Debt - (Gang.CrewManager.I ? Gang.CrewManager.I.DebtPaid : 0)}", UiSkin.Label);
            GUILayout.Space(6);
            if (jm.Active != null)
            {
                GUILayout.Label("<b>Active: " + jm.Active.title + "  ($" + jm.Active.reward + ")</b>", UiSkin.Label); GUILayout.Label(jm.Active.desc, wrap);
                if (GUILayout.Button("Drop job", UiSkin.Button, GUILayout.Height(30))) jm.Abandon();
            }
            else foreach (var j in jm.Offers.ToArray())
            {
                GUILayout.Label($"<b>{j.title}</b>   ${j.reward}", UiSkin.Label); GUILayout.Label(j.desc, wrap);
                if (GUILayout.Button("Accept", UiSkin.ButtonOn, GUILayout.Height(30))) { if (jm.Accept(j)) { open = false; } break; }
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
        }

        void MissionsPage(Rect inner)
        {
            Header("Missions", Page.Home);
            var qm = QuestManager.I; if (!qm) { GUILayout.Label("No missions.", UiSkin.LabelDim); return; }
            scrollList = GUILayout.BeginScrollView(scrollList);
            var wrap = new GUIStyle(UiSkin.LabelDim) { wordWrap = true, fontSize = 12 };
            foreach (var m in qm.View())
            {
                string mark = m.state == 0 ? "[done] " : m.state == 1 ? "[now] " : "";
                GUILayout.Label((m.state == 1 ? "<b>" : "") + mark + m.title + (m.state == 1 ? "</b>" : ""), m.state == 2 ? UiSkin.LabelDim : UiSkin.Label);
                if (m.state != 2) GUILayout.Label(m.desc, wrap);
                GUILayout.Space(6);
            }
            if (qm.AllDone) GUILayout.Label("Chapter 1 complete.", UiSkin.LabelBold);
            GUILayout.EndScrollView();
        }

        void Settings(Rect inner)
        {
            Header("Settings", Page.Home);
            GUILayout.Label($"<b>{PlayerAppearance.PlayerName}</b>", UiSkin.Label);
            GUILayout.Label($"Cash ${EconomyManager.I?.cash ?? 0}   ·   Kills {GameManager.I?.Kills ?? 0}", UiSkin.LabelDim);
            GUILayout.Space(14);
            if (GUILayout.Button("Edit character", UiSkin.ButtonOn, GUILayout.Height(38))) { Close(); if (CharacterCreator.I) CharacterCreator.I.Begin(true); }
            GUILayout.Space(8);
            if (GUILayout.Button("Clear all messages", UiSkin.Button, GUILayout.Height(30))) { convs.Clear(); }
        }
    }
}
