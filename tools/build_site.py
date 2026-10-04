"""Generates the website (site/*.html, site/ru/*.html) from one template and the texts below.

Run after changing texts: python tools/build_site.py
The pages are static; download.js points the download buttons at the newest release.
"""
import html
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "site")
REPO = "https://github.com/zegolka-dev/AimOdometer"
RELEASES = REPO + "/releases"
SITE = "https://zegolka-dev.github.io/AimOdometer"

ICONS = {
    "download": '<path d="M12 3v12"/><path d="m7 10 5 5 5-5"/><path d="M5 21h14"/>',
    "route": '<circle cx="6" cy="19" r="3"/><path d="M9 19h8.5a3.5 3.5 0 0 0 0-7h-11a3.5 3.5 0 0 1 0-7H15"/><circle cx="18" cy="5" r="3"/>',
    "gamepad": '<path d="M6 12h4M8 10v4"/><path d="M15 13h.01M18 11h.01"/><rect x="2" y="6" width="20" height="12" rx="4"/>',
    "trophy": '<path d="M8 21h8M12 17v4"/><path d="M7 4h10v5a5 5 0 0 1-10 0z"/><path d="M17 5h3a3 3 0 0 1-3 4M7 5H4a3 3 0 0 0 3 4"/>',
    "map": '<path d="m3 6 6-3 6 3 6-3v15l-6 3-6-3-6 3z"/><path d="M9 3v15M15 6v15"/>',
    "users": '<circle cx="9" cy="8" r="4"/><path d="M2 21a7 7 0 0 1 14 0"/><path d="M16 4a4 4 0 0 1 0 8M22 21a7 7 0 0 0-5-6.7"/>',
    "zap": '<path d="M13 2 4 14h7l-1 8 9-12h-7z"/>',
    "shield": '<path d="M12 22s8-3.5 8-10V5l-8-3-8 3v7c0 6.5 8 10 8 10z"/><path d="m9 12 2 2 4-4"/>',
    "lock": '<rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
    "github": '<path d="M9 19c-4 1.5-4-2-6-2.5M15 22v-3.9a3.4 3.4 0 0 0-1-2.6c3.1-.3 6.4-1.5 6.4-6.9A5.4 5.4 0 0 0 19 4.8 5 5 0 0 0 18.9 1S17.7.7 15 2.5a13.4 13.4 0 0 0-7 0C5.3.7 4.1 1 4.1 1A5 5 0 0 0 4 4.8 5.4 5.4 0 0 0 2.6 8.6c0 5.4 3.3 6.6 6.4 6.9a3.4 3.4 0 0 0-1 2.6V22"/>',
}


def icon(name):
    return f'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{ICONS[name]}</svg>'


T = {
    "en": dict(
        lang="en", other="ru", other_label="Русский", prefix="",
        title="AimOdometer: how far does your mouse travel?",
        description="Free Windows app for gamers: real mouse distance on the pad per game, per mouse and per day. Achievements, a world map and Steam friends leaderboards.",
        nav_features="Features", nav_safety="Anti-cheat", nav_privacy="Privacy", nav_download="Download", skip="Skip to content",
        eyebrow="Free · open source · Windows 10/11",
        h1='How far does your mouse <span class="glow">really</span> travel?',
        lead="AimOdometer counts the real centimeters your mouse covers on the pad, not pixels on screen: per game, per mouse, per day. It runs quietly in the tray and turns your aim into kilometers.",
        download="Download for Windows", portable="Portable zip",
        meta="Version {version} · {size} MB · Windows 10 22H2+ / 11, 64-bit",
        meta_fallback="Windows 10 22H2+ / 11, 64-bit",
        shot_alt="AimOdometer overview: today's distance, this week, this month, all time and a 14-day chart",
        stats=[("± 1%", "accuracy with your mouse's real DPI"), ("< 10 MB", "of memory in the background"), ("≈ 0.1%", "of one CPU core while you play"), ("0", "hooks, injections or game memory reads")],
        features_title="Every centimeter, counted",
        features_lead="Measured with Windows Raw Input from your mouse itself, so in-game sensitivity and screen resolution do not matter. Only the DPI does, and AimOdometer helps you measure it.",
        features=[
            ("route", "Real distance", "Centimeters, meters and kilometers on the pad, per mouse, with a DPI calibration wizard (a ruler or a bank card is enough)."),
            ("gamepad", "Per game", "Steam games are found automatically, plus 55 others (Riot, Epic, Battle.net…). See which game makes you run the most."),
            ("trophy", "Achievements", "39 achievements: distance, streaks, flicks, night owl, per game. Never pop up during a full-screen game."),
            ("map", "Where would you get?", "Your distance as a walk on a real map, from your city towards a famous one or wherever you choose."),
            ("users", "Friends & world", "Sign in through Steam to compare with your Steam friends and, if you want, the whole world."),
            ("zap", "Featherweight", "A tiny native tracker runs in the background; the window with all the charts opens only when you need it. Updates install themselves."),
        ],
        safety_title="Safe with anti-cheats",
        safety_lead="AimOdometer only reads your mouse the way any mouse software does and notes which app is in front. Nothing touches the game.",
        safety_list=["No hooks, no DLL injection, no overlays", "Never reads or writes game memory", "The keyboard is never read", "Tested with CS2 (VAC), Apex Legends (EAC) and FACEIT"],
        safety_more="How it works and test results",
        privacy_title="Private by default",
        privacy_list=["Your statistics stay on your PC", "No account needed: without Steam sign-in nothing is sent anywhere", "With sign-in only daily totals are synced; who sees you is your choice", "Delete your data any time, locally and in the cloud"],
        privacy_more="Privacy policy",
        gallery_title="Have a look",
        gallery=[("games", "Games: where your mouse runs, with time and km/h"), ("achievements", "Achievements with progress"), ("map", "Where would you get: your month as a walk"), ("stats", "Statistics: records, averages and an activity heat map")],
        faq_title="Questions",
        faq=[
            ("Windows says “Windows protected your PC”", "AimOdometer is not code-signed yet. Click “More info”, then “Run anyway”. The source code is public on GitHub."),
            ("How accurate is it?", "As accurate as the DPI you enter: measure it once in the app with a ruler or a bank card. On Windows 11 a background app gets mouse input merged at ~125 Hz, which costs about 2% while you aim fast; everything else is exact."),
            ("Do I need the internet?", "No. Without Steam sign-in AimOdometer only checks GitHub for updates when its window opens (can be switched off), and the map loads only after you allow it on its tab."),
            ("Where is my data? How do I uninstall?", "Statistics live in %LOCALAPPDATA%\\AimOdometer. Uninstall from Windows Settings › Apps; your statistics stay unless you delete them in AimOdometer's Settings."),
        ],
        final_title="Ready to measure your aim?",
        footer_license="MIT license", footer_source="Source on GitHub",
        privacy_page_title="Privacy policy",
        anticheat_page_title="Anti-cheat safety",
        signing_page_title="Code signing policy", nav_signing="Code signing",
    ),
    "ru": dict(
        lang="ru", other="en", other_label="English", prefix="../",
        title="AimOdometer: сколько проходит твоя мышь?",
        description="Бесплатная программа для геймеров под Windows: реальный пробег мыши по коврику по играм, мышам и дням. Ачивки, карта мира и рейтинги с друзьями из Steam.",
        nav_features="Возможности", nav_safety="Античиты", nav_privacy="Приватность", nav_download="Скачать", skip="Перейти к содержимому",
        eyebrow="Бесплатно · открытый код · Windows 10/11",
        h1='Сколько твоя мышь проходит <span class="glow">на самом деле</span>?',
        lead="AimOdometer считает настоящие сантиметры, которые мышь проходит по коврику, а не пиксели на экране: по играм, по мышам, по дням. Он тихо живёт в трее и превращает твой аим в километры.",
        download="Скачать для Windows", portable="Zip без установки",
        meta="Версия {version} · {size} МБ · Windows 10 22H2+ / 11, 64-бит",
        meta_fallback="Windows 10 22H2+ / 11, 64-бит",
        shot_alt="Обзор AimOdometer: пробег за сегодня, неделю, месяц, всё время и график за 14 дней",
        stats=[("± 1%", "точность с реальным DPI твоей мыши"), ("< 10 МБ", "памяти в фоне"), ("≈ 0,1%", "одного ядра процессора во время игры"), ("0", "хуков, инъекций и чтения памяти игр")],
        features_title="Каждый сантиметр на счету",
        features_lead="Движение читается через Windows Raw Input прямо от мыши, поэтому чувствительность в игре и разрешение экрана не важны. Важен только DPI, и AimOdometer поможет его измерить.",
        features=[
            ("route", "Реальный пробег", "Сантиметры, метры и километры по коврику для каждой мыши, с мастером калибровки DPI (хватит линейки или банковской карты)."),
            ("gamepad", "По играм", "Игры Steam находятся сами, плюс ещё 55 (Riot, Epic, Battle.net…). Видно, в какой игре ты бегаешь мышью больше всего."),
            ("trophy", "Ачивки", "39 ачивок: пробег, серии, флики, ночная сова, по играм. Никогда не всплывают во время полноэкранной игры."),
            ("map", "Куда бы ты дошёл", "Твой пробег как прогулка по настоящей карте: от твоего города к известному или туда, куда выберешь."),
            ("users", "Друзья и мир", "Войди через Steam и сравнивай пробег с друзьями из Steam, а по желанию - со всем миром."),
            ("zap", "Легче пёрышка", "В фоне работает крошечный нативный трекер; окно с графиками открывается, только когда нужно. Обновления ставятся сами."),
        ],
        safety_title="Безопасно для античитов",
        safety_lead="AimOdometer только читает мышь, как любая программа для мышей, и отмечает, какое приложение на переднем плане. Игру он не трогает.",
        safety_list=["Никаких хуков, инъекций DLL и оверлеев", "Не читает и не пишет память игр", "Клавиатура не читается вообще", "Проверен с CS2 (VAC), Apex Legends (EAC) и FACEIT"],
        safety_more="Как это устроено и результаты проверок",
        privacy_title="Приватно по умолчанию",
        privacy_list=["Статистика хранится на твоём ПК", "Аккаунт не нужен: без входа через Steam ничего никуда не отправляется", "После входа синхронизируются только итоги за день; кто тебя видит - решаешь ты", "Данные можно удалить в любой момент - на ПК и в облаке"],
        privacy_more="Политика приватности",
        gallery_title="Как это выглядит",
        gallery=[("games", "Игры: где бегает твоя мышь, со временем и км/ч"), ("achievements", "Ачивки с прогрессом"), ("map", "Куда бы ты дошёл: твой месяц как прогулка"), ("stats", "Статистика: рекорды, средние и тепловая карта активности")],
        faq_title="Вопросы",
        faq=[
            ("Windows пишет «Windows защитила ваш компьютер»", "У AimOdometer пока нет цифровой подписи. Нажми «Подробнее», затем «Выполнить в любом случае». Исходный код открыт на GitHub."),
            ("Насколько это точно?", "Настолько, насколько точен указанный DPI: измерь его один раз в программе линейкой или банковской картой. В Windows 11 фоновые программы получают мышь склеенной до ~125 Гц, это около 2% при очень быстром аиме; всё остальное считается точно."),
            ("Нужен ли интернет?", "Нет. Без входа через Steam AimOdometer только проверяет обновления на GitHub при открытии окна (можно отключить), а карта загружается лишь после разрешения на её вкладке."),
            ("Где мои данные? Как удалить программу?", "Статистика лежит в %LOCALAPPDATA%\\AimOdometer. Удалить программу можно в Параметрах Windows › Приложения; статистика останется, если не удалить её в настройках AimOdometer."),
        ],
        final_title="Готов измерить свой аим?",
        footer_license="Лицензия MIT", footer_source="Код на GitHub",
        privacy_page_title="Политика приватности",
        anticheat_page_title="Безопасность для античитов",
        signing_page_title="Политика подписи кода", nav_signing="Подпись кода",
    ),
}


def page(t, file, title, description, body):
    p = t["prefix"]
    other_href = ("ru/" if t["lang"] == "en" else "../") + file
    canonical = SITE + "/" + ("ru/" if t["lang"] == "ru" else "") + file.replace("index.html", "")
    return f"""<!doctype html>
<html lang="{t['lang']}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{html.escape(title)}</title>
<meta name="description" content="{html.escape(description)}">
<link rel="canonical" href="{canonical}">
<link rel="alternate" hreflang="en" href="{SITE}/{file.replace('index.html', '')}">
<link rel="alternate" hreflang="ru" href="{SITE}/ru/{file.replace('index.html', '')}">
<meta property="og:title" content="{html.escape(title)}">
<meta property="og:description" content="{html.escape(description)}">
<meta property="og:image" content="{SITE}/assets/og.png">
<meta property="og:type" content="website">
<meta name="theme-color" content="#0F0F23">
<link rel="icon" type="image/png" href="{p}assets/favicon.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;600&family=Russo+One&display=swap" rel="stylesheet">
<link rel="stylesheet" href="{p}style.css">
</head>
<body>
<a class="skip" href="#main">{t['skip']}</a>
<header class="top">
  <div class="wrap">
    <a class="brand" href="{p}{'ru/' if t['lang'] == 'ru' else ''}index.html"><img src="{p}assets/logo.png" alt="" width="32" height="32">AimOdometer</a>
    <nav aria-label="Main">
      <a href="{p}{'ru/' if t['lang'] == 'ru' else ''}index.html#features">{t['nav_features']}</a>
      <a href="anticheat.html">{t['nav_safety']}</a>
      <a href="privacy.html">{t['nav_privacy']}</a>
      <a class="lang" href="{other_href}" hreflang="{t['other']}" lang="{t['other']}">{t['other_label']}</a>
    </nav>
  </div>
</header>
<main id="main">
{body}
</main>
<footer>
  <div class="wrap">
    <span>© 2026 AimOdometer · {t['footer_license']}</span>
    <nav aria-label="Footer">
      <a href="{REPO}">{t['footer_source']}</a>
      <a href="privacy.html">{t['nav_privacy']}</a>
      <a href="anticheat.html">{t['nav_safety']}</a>
      <a href="code-signing.html">{t['nav_signing']}</a>
      <a href="{RELEASES}">Releases</a>
    </nav>
  </div>
</footer>
<script src="{p}download.js" defer></script>
</body>
</html>
"""


def download_buttons(t):
    return f"""<div class="cta">
      <a class="btn btn-primary" href="{RELEASES}" data-asset="AimOdometerApp-win-Setup.exe">{icon('download')}{t['download']}</a>
      <a class="btn btn-ghost" href="{RELEASES}" data-asset="AimOdometerApp-win-Portable.zip">{t['portable']}</a>
    </div>
    <p class="meta" data-release="{html.escape(t['meta'])}">{t['meta_fallback']}</p>"""


def landing(t):
    p = t["prefix"]
    lang = t["lang"]
    stats = "".join(f'<div class="stat"><b>{html.escape(a)}</b><span>{html.escape(b)}</span></div>' for a, b in t["stats"])
    features = "".join(f'<article class="card"><div class="icon">{icon(i)}</div><h3>{html.escape(h)}</h3><p>{html.escape(d)}</p></article>' for i, h, d in t["features"])
    safety = "".join(f"<li>{html.escape(x)}</li>" for x in t["safety_list"])
    privacy = "".join(f"<li>{html.escape(x)}</li>" for x in t["privacy_list"])
    gallery = "".join(
        f'<figure><img class="shot" src="{p}assets/shots/{n}-{lang}.webp" alt="{html.escape(c)}" width="1180" height="760" loading="lazy" decoding="async"><figcaption>{html.escape(c)}</figcaption></figure>'
        for n, c in t["gallery"])
    faq = "".join(f"<details><summary>{html.escape(q)}</summary><p>{html.escape(a)}</p></details>" for q, a in t["faq"])
    body = f"""<section class="hero" aria-labelledby="hero-title">
  <div class="wrap">
    <div>
      <p class="eyebrow">{html.escape(t['eyebrow'])}</p>
      <h1 id="hero-title">{t['h1']}</h1>
      <p class="lead">{html.escape(t['lead'])}</p>
      {download_buttons(t)}
    </div>
    <img class="shot" src="{p}assets/shots/overview-{lang}.webp" alt="{html.escape(t['shot_alt'])}" width="1180" height="760" fetchpriority="high">
  </div>
  <div class="wrap"><div class="stats">{stats}</div></div>
</section>
<section id="features" aria-labelledby="features-title">
  <div class="wrap">
    <h2 id="features-title">{html.escape(t['features_title'])}</h2>
    <p class="section-lead">{html.escape(t['features_lead'])}</p>
    <div class="grid">{features}</div>
  </div>
</section>
<section aria-labelledby="safety-title">
  <div class="wrap split">
    <div>
      <div class="icon">{icon('shield')}</div>
      <h2 id="safety-title" style="margin-top:14px">{html.escape(t['safety_title'])}</h2>
      <p class="section-lead">{html.escape(t['safety_lead'])}</p>
      <ul class="list ok">{safety}</ul>
      <p><a href="anticheat.html">{html.escape(t['safety_more'])} →</a></p>
    </div>
    <div>
      <div class="icon">{icon('lock')}</div>
      <h2 style="margin-top:14px">{html.escape(t['privacy_title'])}</h2>
      <ul class="list">{privacy}</ul>
      <p><a href="privacy.html">{html.escape(t['privacy_more'])} →</a></p>
    </div>
  </div>
</section>
<section aria-labelledby="gallery-title">
  <div class="wrap">
    <h2 id="gallery-title">{html.escape(t['gallery_title'])}</h2>
    <div class="gallery">{gallery}</div>
  </div>
</section>
<section aria-labelledby="faq-title">
  <div class="wrap">
    <h2 id="faq-title">{html.escape(t['faq_title'])}</h2>
    {faq}
  </div>
</section>
<section class="final" aria-labelledby="final-title">
  <div class="wrap">
    <h2 id="final-title">{html.escape(t['final_title'])}</h2>
    {download_buttons(t)}
  </div>
</section>"""
    return page(t, "index.html", t["title"], t["description"], body)


def document(t, file, title, inner_html):
    body = f'<div class="wrap doc">\n<h1>{html.escape(title)}</h1>\n{inner_html}\n</div>'
    return page(t, file, f"{title} · AimOdometer", t["description"], body)


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


if __name__ == "__main__":
    for code, t in T.items():
        folder = ROOT if code == "en" else os.path.join(ROOT, "ru")
        write(os.path.join(folder, "index.html"), landing(t))
        for file, key in (("privacy.html", "privacy_page_title"), ("anticheat.html", "anticheat_page_title"),
                          ("code-signing.html", "signing_page_title")):
            source = os.path.join(ROOT, "content", f"{file.replace('.html', '')}.{code}.html")
            with open(source, encoding="utf-8") as f:
                write(os.path.join(folder, file), document(t, file, t[key], f.read()))
    print("site built")
