"""Generates the website (site/*.html, site/ru/*.html) from one template and the texts below.

Run after changing texts: python tools/build_site.py (and python tools/make_site_tiles.py after new screenshots).
The pages are static: site.js adds the motion (fade-ins, scroll marquee, magnetic hero, letter-by-letter text,
stacking cards; all off with "reduce motion"), download.js points the download buttons at the newest release.
"""
import datetime
import hashlib
import html
import json
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "site")
REPO = "https://github.com/zegolka-dev/AimOdometer"
RELEASES = REPO + "/releases"
SITE = "https://zegolka-dev.github.io/AimOdometer"

ICONS = {
    "download": '<path d="M12 3v12"/><path d="m7 10 5 5 5-5"/><path d="M5 21h14"/>',
    "mouse": '<rect x="5" y="2" width="14" height="20" rx="7"/><path d="M12 6v4"/>',
    "trophy": '<path d="M8 21h8M12 17v4"/><path d="M7 4h10v5a5 5 0 0 1-10 0z"/><path d="M17 5h3a3 3 0 0 1-3 4M7 5H4a3 3 0 0 0 3 4"/>',
    "pin": '<path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0z"/><circle cx="12" cy="10" r="3"/>',
    "crosshair": '<circle cx="12" cy="12" r="10"/><path d="M22 12h-4M6 12H2M12 6V2M12 22v-4"/>',
    "shield": '<path d="M12 22s8-3.5 8-10V5l-8-3-8 3v7c0 6.5 8 10 8 10z"/><path d="m9 12 2 2 4-4"/>',
    "lock": '<rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
    "arrow": '<path d="M5 12h14"/><path d="m13 6 6 6-6 6"/>',
    "globe": '<circle cx="12" cy="12" r="10"/><path d="M2 12h20"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>',
}

# Search engine ownership checks (the content value of the meta tag the console shows). Empty = not added.
GOOGLE_VERIFICATION = "Kj0CfCkvA-06Pz4pwEgqz77r_XV53aLWFht5Sv82sV0"
YANDEX_VERIFICATION = ""

# English pages only: a browser set to Russian goes to the Russian page, unless a language was chosen before
# (the language button remembers the choice).
REDIRECT = ('<script>try{if(!localStorage.getItem("lang")&&/^ru\\b/i.test(navigator.language||""))'
            'location.replace("ru/"+location.pathname.split("/").pop()+location.hash)}catch(e){}</script>')


def asset(prefix, name):
    """A site file with a version from its content, so browsers never keep an old copy after a deploy."""
    with open(os.path.join(ROOT, name), "rb") as f:
        return f"{prefix}{name}?v={hashlib.sha256(f.read()).hexdigest()[:10]}"


def icon(name):
    return f'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{ICONS[name]}</svg>'


def lang_button(t, href):
    """The language switch: a globe and RU/EN in a pill; clicking remembers the choice for the redirect above."""
    remember = f"try{{localStorage.setItem('lang','{t['other']}')}}catch(e){{}}"
    return (f'<a class="lang" href="{href}" hreflang="{t["other"]}" lang="{t["other"]}" '
            f'aria-label="{html.escape(t["lang_title"])}" title="{html.escape(t["lang_title"])}" onclick="{remember}">'
            f'{icon("globe")}<span>{t["other_short"]}</span></a>')


# Marquee tiles (tools/make_site_tiles.py): row one moves right, row two left.
ROW_ONE = ["overview-today", "games-share", "stats-days", "achievements-grid", "map-route", "overview-days"]
ROW_TWO = ["stats-totals", "games-list", "achievements-progress", "map-top", "overview-totals"]

T = {
    "en": dict(
        lang="en", other="ru", other_short="RU", lang_title="Читать на русском", prefix="",
        title="AimOdometer: Mouse Odometer for Gamers, Free for Windows",
        description="AimOdometer is a free mouse odometer for Windows gamers: how far your mouse really travels on the pad, per game and per mouse. Achievements, Steam leaderboards.",
        og_locale="en_US",
        nav_features="Features", nav_inside="Inside", nav_safety="Anti-cheat", nav_privacy="Privacy", nav_download="Download",
        skip="Skip to content",
        hero_lead="Counts how far your mouse really travels on the pad, per game and per mouse",
        hero_alt="AimOdometer's overview: distance today, this week, this month, all time and a 14-day chart",
        chip_today="today", chip_ach="achievements",
        download="Download", download_long="Download for Windows", portable="Portable zip",
        signing_note='Code signing: free code signing provided by <a href="https://signpath.io">SignPath.io</a>, certificate by <a href="https://signpath.org">SignPath Foundation</a> (being set up; see the <a href="code-signing.html">code signing policy</a>).',
        meta="Version {version} · {size} MB · Windows 10 22H2+ / 11, 64-bit",
        meta_fallback="Windows 10 22H2+ / 11, 64-bit",
        marquee_label="Screens of AimOdometer",
        about_title="What is it",
        about_text="AimOdometer is an odometer for your mouse that turns your aim into kilometers. A tiny counter in the tray reads your mouse the way mouse software does and converts every movement into real centimeters on the pad using your DPI, even in shooters where the cursor is locked to the center. See which game makes you run the most, beat your records and race your Steam friends.",
        stats=[("± 1%", "accuracy with your mouse's real DPI"), ("< 10 MB", "of memory in the background"), ("≈ 0.1%", "of one CPU core while you play"), ("0", "hooks, injections or game memory reads")],
        features_title="Features",
        features=[
            ("Real distance", "Centimeters, meters and kilometers on the pad for every mouse, measured with Windows Raw Input. In-game sensitivity and screen resolution do not matter, only the DPI, and a calibration wizard measures it with a ruler or a bank card."),
            ("Per game", "Steam games are found automatically, plus 55 others from Riot, Epic, Battle.net and more. Games that run as administrator, like Genshin Impact, are counted too."),
            ("Achievements and fair play", "39 achievements for distance, streaks, flicks, night sessions and games. Inflating kilometers with a DPI set far too low earns shame badges instead: Clown, Fool, Blockhead and Booster."),
            ("World map", "Your distance as a walk along real roads, from your city towards a famous one or wherever you choose."),
            ("Friends and leaderboards", "Sign in with Steam to compare with your Steam friends and, if you want, the whole world. Every line shows the DPI behind the distance and the fastest flick."),
            ("Gear wear", "Add your mouse pad, arm sleeve, glides and mouse: AimOdometer counts the kilometers each one has done and reminds you with a notification when one reaches 90% of its lifetime, so you know when to replace it."),
            ("Featherweight", "A native counter of a few megabytes runs in the background and never touches the game; the window with charts opens only when you need it. Updates install themselves."),
        ],
        inside_title="Inside",
        inside=[
            ("Statistics", "Every day in numbers", ("overview-days", "Distance of the last 14 days"), ("games-list", "Distance per game with time and km/h"), ("card-overview", "Overview: today, this week, this month and all time")),
            ("Achievements", "Goals worth chasing", ("achievements-progress", "Achievements with progress bars"), ("stats-totals", "Totals for any period"), ("card-achievements", "The achievements page")),
            ("Map", "Your aim as a journey", ("map-route", "The route on a real map"), ("overview-totals", "All-time distance compared with famous places"), ("card-map", "Where would you get: your month as a walk")),
        ],
        trust_title="Safe and private",
        safety_title="Safe with anti-cheats",
        safety_list=["Reads the mouse the way any mouse software does, nothing else", "No hooks, no DLL injection, no overlays", "Never reads or writes game memory; the keyboard is never read", "Tested with VAC, FACEIT, Easy Anti-Cheat, Riot Vanguard and BattlEye"],
        safety_more="How it works and test results",
        privacy_title="Private by default",
        privacy_list=["Your statistics stay on your PC", "No account needed: without Steam sign-in nothing is sent anywhere", "With sign-in only daily totals are synced; who sees you is your choice", "Delete your data any time, on the PC and in the cloud"],
        privacy_more="Privacy policy",
        faq_title="Questions",
        faq=[
            ("Windows says “Windows protected your PC”", "AimOdometer is not code-signed yet (signing through the SignPath Foundation is being set up). Click “More info”, then “Run anyway”. The source code is public on GitHub."),
            ("How accurate is it?", "As accurate as the DPI you enter: measure it once in the app with a ruler or a bank card. On Windows 11 a background app gets mouse input merged, which costs about 2% while you aim fast; everything else is exact."),
            ("Do I need the internet?", "No. Without Steam sign-in AimOdometer only checks GitHub for updates (can be switched off), and the map loads only after you allow it on its tab."),
            ("Where is my data? How do I uninstall?", "Statistics live in %LOCALAPPDATA%\\AimOdometer. Uninstall from Windows Settings › Apps; your statistics stay unless you delete them in AimOdometer's Settings."),
        ],
        final_title="Measure your aim",
        footer_license="MIT license", footer_source="Source on GitHub",
        privacy_page_title="Privacy policy",
        anticheat_page_title="Anti-cheat safety",
        signing_page_title="Code signing policy", nav_signing="Code signing",
    ),
    "ru": dict(
        lang="ru", other="en", other_short="EN", lang_title="Read in English", prefix="../",
        title="AimOdometer (Аим Одометр): одометр мыши для геймеров",
        description="AimOdometer (Аим Одометр) - бесплатный одометр мыши для Windows: сколько мышь реально проходит по коврику в каждой игре. Ачивки и рейтинги друзей из Steam.",
        og_locale="ru_RU",
        nav_features="Возможности", nav_inside="Внутри", nav_safety="Античиты", nav_privacy="Приватность", nav_download="Скачать",
        skip="Перейти к содержимому",
        hero_lead="Считает, сколько твоя мышь на самом деле проходит по коврику, по играм и по мышам",
        hero_alt="Обзор AimOdometer: пробег за сегодня, неделю, месяц, всё время и график за 14 дней",
        chip_today="сегодня", chip_ach="ачивок",
        download="Скачать", download_long="Скачать для Windows", portable="Zip без установки",
        signing_note='Подпись кода: бесплатно от <a href="https://signpath.io">SignPath.io</a>, сертификат от <a href="https://signpath.org">SignPath Foundation</a> (подключается; см. <a href="code-signing.html">политику подписи кода</a>).',
        meta="Версия {version} · {size} МБ · Windows 10 22H2+ / 11, 64-бит",
        meta_fallback="Windows 10 22H2+ / 11, 64-бит",
        marquee_label="Экраны AimOdometer",
        about_title="Что это",
        about_text="AimOdometer (Аим Одометр) - это одометр для мыши, который превращает твой аим в километры. Крошечный счётчик в трее читает мышь так же, как программы для мышей, и переводит каждое движение в настоящие сантиметры по коврику с учётом DPI, даже в шутерах, где курсор зажат в центре. Смотри, в какой игре ты бегаешь мышью больше всего, бей свои рекорды и соревнуйся с друзьями из Steam.",
        stats=[("± 1%", "точность с реальным DPI твоей мыши"), ("< 10 МБ", "памяти в фоне"), ("≈ 0,1%", "одного ядра процессора во время игры"), ("0", "хуков, инъекций и чтения памяти игр")],
        features_title="Возможности",
        features=[
            ("Реальный пробег", "Сантиметры, метры и километры по коврику для каждой мыши, через Windows Raw Input. Чувствительность в игре и разрешение экрана не важны, важен только DPI, а мастер калибровки измерит его линейкой или банковской картой."),
            ("По играм", "Игры Steam находятся сами, плюс ещё 55 из Riot, Epic, Battle.net и других. Игры, запущенные от имени администратора, вроде Genshin Impact, тоже считаются."),
            ("Ачивки и честная игра", "39 ачивок за пробег, серии, флики, ночные сессии и игры. А за километры, накрученные слишком низким DPI, дают позорные значки: Клоун, Дурак, Балбес и Накрутчик."),
            ("Карта мира", "Твой пробег как прогулка по настоящим дорогам: от твоего города к известному или туда, куда выберешь."),
            ("Друзья и рейтинги", "Войди через Steam и сравнивай пробег с друзьями, а по желанию со всем миром. В каждой строке виден DPI, на котором набегано расстояние и поставлен рекорд флика."),
            ("Износ снаряжения", "Добавь коврик, рукав, глайды и мышь: AimOdometer считает, сколько километров прошла каждая вещь, и напомнит уведомлением, когда одна из них израсходует 90% ресурса, чтобы ты знал, когда её менять."),
            ("Легче пёрышка", "Нативный счётчик в несколько мегабайт работает в фоне и никогда не трогает игру; окно с графиками открывается, только когда нужно. Обновления ставятся сами."),
        ],
        inside_title="Внутри",
        inside=[
            ("Статистика", "Каждый день в цифрах", ("overview-days", "Пробег за последние 14 дней"), ("games-list", "Пробег по играм со временем и км/ч"), ("card-overview", "Обзор: сегодня, неделя, месяц и всё время")),
            ("Ачивки", "Цели, за которыми интересно гнаться", ("achievements-progress", "Ачивки с прогрессом"), ("stats-totals", "Итоги за любой период"), ("card-achievements", "Страница ачивок")),
            ("Карта", "Твой аим как путешествие", ("map-route", "Маршрут на настоящей карте"), ("overview-totals", "Пробег за всё время в сравнении с известными местами"), ("card-map", "Куда бы ты дошёл: твой месяц как прогулка")),
        ],
        trust_title="Безопасно и приватно",
        safety_title="Безопасно для античитов",
        safety_list=["Читает мышь так же, как любая программа для мышей, и больше ничего", "Никаких хуков, инъекций DLL и оверлеев", "Не читает и не пишет память игр; клавиатура не читается вообще", "Проверен с VAC, FACEIT, Easy Anti-Cheat, Riot Vanguard и BattlEye"],
        safety_more="Как это устроено и результаты проверок",
        privacy_title="Приватно по умолчанию",
        privacy_list=["Статистика хранится на твоём ПК", "Аккаунт не нужен: без входа через Steam ничего никуда не отправляется", "После входа синхронизируются только итоги за день; кто тебя видит, решаешь ты", "Данные можно удалить в любой момент, на ПК и в облаке"],
        privacy_more="Политика приватности",
        faq_title="Вопросы",
        faq=[
            ("Windows пишет «Windows защитила ваш компьютер»", "У AimOdometer пока нет цифровой подписи (подпись через SignPath Foundation подключается). Нажми «Подробнее», затем «Выполнить в любом случае». Исходный код открыт на GitHub."),
            ("Насколько это точно?", "Настолько, насколько точен указанный DPI: измерь его один раз в программе линейкой или банковской картой. В Windows 11 фоновые программы получают движения мыши склеенными, это около 2% при очень быстром аиме; всё остальное считается точно."),
            ("Нужен ли интернет?", "Нет. Без входа через Steam AimOdometer только проверяет обновления на GitHub (можно отключить), а карта загружается лишь после разрешения на её вкладке."),
            ("Где мои данные? Как удалить программу?", "Статистика лежит в %LOCALAPPDATA%\\AimOdometer. Удалить программу можно в Параметрах Windows › Приложения; статистика останется, если не удалить её в настройках AimOdometer."),
        ],
        final_title="Измерь свой аим",
        footer_license="Лицензия MIT", footer_source="Код на GitHub",
        privacy_page_title="Политика приватности",
        anticheat_page_title="Безопасность для античитов",
        signing_page_title="Политика подписи кода", nav_signing="Подпись кода",
    ),
}


def structured_data(t):
    """schema.org data for search engines: the app (name variants, free, Windows) and the site's name."""
    url = SITE + ("/ru/" if t["lang"] == "ru" else "/")
    app = {
        "@context": "https://schema.org",
        "@type": "SoftwareApplication",
        "name": "AimOdometer",
        "alternateName": ["Aim Odometer", "Аим Одометр", "АимОдометр"],
        "description": t["description"],
        "url": url,
        "image": SITE + "/assets/og.png",
        "applicationCategory": "GameApplication",
        "operatingSystem": "Windows 10, Windows 11",
        "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"},
        "downloadUrl": RELEASES,
        "license": "https://opensource.org/licenses/MIT",
        "isAccessibleForFree": True,
        "inLanguage": ["en", "ru"],
        "sameAs": [REPO],
    }
    site = {
        "@context": "https://schema.org",
        "@type": "WebSite",
        "name": "AimOdometer",
        "alternateName": ["Aim Odometer", "Аим Одометр"],
        "url": SITE + "/",
    }
    return "".join(f'<script type="application/ld+json">{json.dumps(x, ensure_ascii=False)}</script>\n' for x in (app, site))


def fade(delay=0.0, x=0, y=30, cls=""):
    """Class and style attributes for a fade-in (site.js); x/y are the start offset in px."""
    return f'class="{(cls + " fade").strip()}" style="--d:{delay}s;--x:{x}px;--y:{y}px"'


def page(t, file, title, description, body, landing=False):
    p = t["prefix"]
    other_href = ("ru/" if t["lang"] == "en" else "../") + file
    canonical = SITE + "/" + ("ru/" if t["lang"] == "ru" else "") + file.replace("index.html", "")
    header = "" if landing else f"""<header class="top">
  <div class="wrap">
    <a class="brand" href="index.html"><img src="{p}assets/logo.png" alt="" width="32" height="32">AimOdometer</a>
    <nav aria-label="Main">
      <a href="index.html#features">{t['nav_features']}</a>
      <a href="anticheat.html">{t['nav_safety']}</a>
      <a href="privacy.html">{t['nav_privacy']}</a>
      {lang_button(t, other_href)}
    </nav>
  </div>
</header>
"""
    return f"""<!doctype html>
<html lang="{t['lang']}">
<head>
<meta charset="utf-8">
{REDIRECT if t['lang'] == 'en' else ''}
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{html.escape(title)}</title>
<meta name="description" content="{html.escape(description)}">
<link rel="canonical" href="{canonical}">
<link rel="alternate" hreflang="en" href="{SITE}/{file.replace('index.html', '')}">
<link rel="alternate" hreflang="ru" href="{SITE}/ru/{file.replace('index.html', '')}">
<link rel="alternate" hreflang="x-default" href="{SITE}/{file.replace('index.html', '')}">
<meta property="og:title" content="{html.escape(title)}">
<meta property="og:description" content="{html.escape(description)}">
<meta property="og:image" content="{SITE}/assets/og.png">
<meta property="og:type" content="website">
<meta property="og:url" content="{canonical}">
<meta property="og:site_name" content="AimOdometer">
<meta property="og:locale" content="{t['og_locale']}">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="{html.escape(title)}">
<meta name="twitter:description" content="{html.escape(description)}">
<meta name="twitter:image" content="{SITE}/assets/og.png">
{f'<meta name="google-site-verification" content="{GOOGLE_VERIFICATION}">' if GOOGLE_VERIFICATION else ''}
{f'<meta name="yandex-verification" content="{YANDEX_VERIFICATION}">' if YANDEX_VERIFICATION else ''}
{structured_data(t) if landing else ''}
<meta name="theme-color" content="#0F0F23">
<link rel="icon" type="image/png" href="{p}assets/favicon.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Exo+2:wght@300..900&display=swap" rel="stylesheet">
<link rel="stylesheet" href="{asset(p, 'style.css')}">
{'<script>document.documentElement.classList.add("js")</script>' if landing else ''}
</head>
<body{' class="landing"' if landing else ''}>
<a class="skip" href="#main">{t['skip']}</a>
{header}<main id="main">
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
<script src="{asset(p, 'download.js')}" defer></script>
{f'<script src="{asset(p, "site.js")}" defer></script>' if landing else ''}
</body>
</html>
"""


def glow_button(t, label):
    return f'<a class="btn-glow" href="{RELEASES}" data-asset="AimOdometerApp-win-Setup.exe">{icon("download")}<span>{html.escape(label)}</span></a>'


def tile(p, name, lang, alt=""):
    return f'<img src="{asset(p, f'assets/tiles/{name}-{lang}.webp')}" alt="{html.escape(alt)}" width="840" height="540" loading="lazy" decoding="async">'


def landing(t):
    p = t["prefix"]
    lang = t["lang"]
    other_href = ("ru/" if lang == "en" else "../") + "index.html"

    def row(names, direction):
        tiles = "".join(f'<div class="tile">{tile(p, n, lang)}</div>' for n in names * 3)
        return f'<div class="marquee-row" data-direction="{direction}">{tiles}</div>'

    letters = "".join(f'<span class="ch">{html.escape(c)}</span>' if c != " " else " " for c in t["about_text"])
    stats = "".join(
        f'<div {fade(0.1 * i, 0, 24)}><b>{html.escape(a)}</b><span>{html.escape(b)}</span></div>'
        for i, (a, b) in enumerate(t["stats"]))
    features = "".join(
        f'<li {fade(0.1 * i)}><span class="num" aria-hidden="true">{i + 1:02d}</span>'
        f'<div><h3>{html.escape(name)}</h3><p>{html.escape(text)}</p></div></li>'
        for i, (name, text) in enumerate(t["features"]))
    cards = "".join(
        f"""<div class="stack-slot">
      <article class="stack-card" style="--i:{i}" aria-labelledby="inside-{i}">
        <div class="card-head">
          <span class="num" aria-hidden="true">{i + 1:02d}</span>
          <div class="card-title"><span class="kicker">{html.escape(kind)}</span><h3 id="inside-{i}">{html.escape(name)}</h3></div>
          <a class="btn-ghost" href="#download">{html.escape(t['download'])}</a>
        </div>
        <div class="card-grid">
          <div class="col-left">
            <figure class="pic a">{tile(p, a[0], lang, a[1])}</figure>
            <figure class="pic b">{tile(p, b[0], lang, b[1])}</figure>
          </div>
          <figure class="pic c">{tile(p, c[0], lang, c[1])}</figure>
        </div>
      </article>
    </div>"""
        for i, (kind, name, a, b, c) in enumerate(t["inside"]))
    safety = "".join(f"<li>{html.escape(x)}</li>" for x in t["safety_list"])
    privacy = "".join(f"<li>{html.escape(x)}</li>" for x in t["privacy_list"])
    faq = "".join(f"<details {fade(0.08 * i, 0, 20)}><summary>{html.escape(q)}</summary><p>{html.escape(a)}</p></details>"
                  for i, (q, a) in enumerate(t["faq"]))
    stickers = "".join(
        f'<div {fade(d, x, 0, f"sticker s{i}")} aria-hidden="true"><span class="sticker-face">{icon(name)}</span></div>'
        for i, (name, d, x) in enumerate([("mouse", 0.1, -80), ("trophy", 0.15, 80), ("pin", 0.25, -80), ("crosshair", 0.3, 80)]))

    body = f"""<section class="hero" aria-labelledby="hero-title">
  <nav class="hero-nav" aria-label="Main">
    <div {fade(0, 0, -20)}>
      <a href="#features">{t['nav_features']}</a>
      <a class="optional" href="#inside">{t['nav_inside']}</a>
      <a class="optional" href="anticheat.html">{t['nav_safety']}</a>
      <a href="#download">{t['nav_download']}</a>
      {lang_button(t, other_href)}
    </div>
  </nav>
  <div class="hero-title">
    <h1 id="hero-title" {fade(0.15, 0, 40)}><span class="giant">AimOdometer</span></h1>
  </div>
  <div {fade(0.6, 0, 30, "hero-visual")}>
    <div class="magnet" data-magnet>
      <img src="{p}assets/shots/overview-{lang}.webp" alt="{html.escape(t['hero_alt'])}" width="1180" height="760" fetchpriority="high">
      <span class="chip chip-a" aria-hidden="true"><b>573 m</b> {html.escape(t['chip_today'])}</span>
      <span class="chip chip-b" aria-hidden="true"><b>28/39</b> {html.escape(t['chip_ach'])}</span>
    </div>
  </div>
  <div class="hero-bottom">
    <p {fade(0.35, 0, 20, "hero-lead")}>{html.escape(t['hero_lead'])}</p>
    <div {fade(0.5, 0, 20)}>{glow_button(t, t['download'])}</div>
  </div>
</section>

<section class="marquee" aria-label="{html.escape(t['marquee_label'])}">
  <div aria-hidden="true">
    {row(ROW_ONE, 'right')}
    {row(ROW_TWO, 'left')}
  </div>
</section>

<section class="about" aria-labelledby="about-title">
  {stickers}
  <div class="about-inner">
    <h2 id="about-title" {fade(0, 0, 40)}><span class="giant">{html.escape(t['about_title'])}</span></h2>
    <p class="reveal"><span class="sr-only">{html.escape(t['about_text'])}</span><span class="reveal-text" aria-hidden="true">{letters}</span></p>
    <div class="stats">{stats}</div>
    <div {fade(0.2, 0, 20)}>{glow_button(t, t['download_long'])}</div>
  </div>
</section>

<section class="features" id="features" aria-labelledby="features-title">
  <h2 id="features-title" {fade(0, 0, 40)}>{html.escape(t['features_title'])}</h2>
  <ol>{features}</ol>
</section>

<section class="inside" id="inside" aria-labelledby="inside-title">
  <h2 id="inside-title" {fade(0, 0, 40)}><span class="giant">{html.escape(t['inside_title'])}</span></h2>
  <div class="stack">{cards}</div>
</section>

<section class="trust" aria-labelledby="trust-title">
  <h2 id="trust-title" {fade(0, 0, 40)}><span class="giant">{html.escape(t['trust_title'])}</span></h2>
  <div class="trust-grid">
    <div {fade(0.1, cls="panel")}>
      <span class="panel-icon">{icon('shield')}</span>
      <h3>{html.escape(t['safety_title'])}</h3>
      <ul>{safety}</ul>
      <a class="more" href="anticheat.html">{html.escape(t['safety_more'])} {icon('arrow')}</a>
    </div>
    <div {fade(0.2, cls="panel")}>
      <span class="panel-icon">{icon('lock')}</span>
      <h3>{html.escape(t['privacy_title'])}</h3>
      <ul>{privacy}</ul>
      <a class="more" href="privacy.html">{html.escape(t['privacy_more'])} {icon('arrow')}</a>
    </div>
  </div>
</section>

<section class="faq" aria-labelledby="faq-title">
  <h2 id="faq-title" {fade(0, 0, 40)}><span class="giant">{html.escape(t['faq_title'])}</span></h2>
  <div class="faq-list">{faq}</div>
</section>

<section class="final" id="download" aria-labelledby="final-title">
  <h2 id="final-title" {fade(0, 0, 40)}><span class="giant">{html.escape(t['final_title'])}</span></h2>
  <div {fade(0.15, 0, 20, "final-actions")}>
    {glow_button(t, t['download_long'])}
    <a class="btn-ghost" href="{RELEASES}" data-asset="AimOdometerApp-win-Portable.zip">{html.escape(t['portable'])}</a>
  </div>
  <p class="meta" data-release="{html.escape(t['meta'])}">{t['meta_fallback']}</p>
  <p class="meta signing">{t['signing_note']}</p>
</section>"""
    return page(t, "index.html", t["title"], t["description"], body, landing=True)


def document(t, file, title, inner_html):
    body = f'<div class="wrap doc">\n<h1>{html.escape(title)}</h1>\n{inner_html}\n</div>'
    return page(t, file, f"{title} · AimOdometer", t["description"], body)


PAGES = ["", "privacy.html", "anticheat.html", "code-signing.html"]


def sitemap():
    """Every page in both languages with its alternates (hreflang) and today's date."""
    today = datetime.date.today().isoformat()
    entries = []
    for page_name in PAGES:
        for prefix in ("", "ru/"):
            links = "".join(
                f'\n    <xhtml:link rel="alternate" hreflang="{code}" href="{SITE}/{p}{page_name}"/>'
                for code, p in (("en", ""), ("ru", "ru/"), ("x-default", "")))
            priority = "1.0" if page_name == "" else "0.6"
            entries.append(f"  <url>\n    <loc>{SITE}/{prefix}{page_name}</loc>\n    <lastmod>{today}</lastmod>\n"
                           f"    <priority>{priority}</priority>{links}\n  </url>")
    return ('<?xml version="1.0" encoding="UTF-8"?>\n'
            '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n'
            + "\n".join(entries) + "\n</urlset>\n")


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
    write(os.path.join(ROOT, "sitemap.xml"), sitemap())
    print("site built")
