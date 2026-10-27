"""Refresh manual HTML and install notices without touching showcase images."""
import argparse
from pathlib import Path
import re

from bs4 import BeautifulSoup
import mistune

BASE = 'https://github.com/tomTom1010-IEE/TomRenderInfrastructure/releases/tag/v0.1.0'
CHAPTERS = ['Installation.md','Material-Guide.md','HardSurface-Release.md',
            'KKS-ME-Acceptance.md','Troubleshooting.md','Build-Guide.md','Release-Status.md']
markdown = mistune.create_markdown(plugins=['table', 'strikethrough', 'url'])


def refresh_manual(path, content, source_base):
    soup = BeautifulSoup(path.read_text(encoding='utf-8-sig'), 'html.parser')
    old = {' '.join(h.get_text().split()):h.get('id') for h in soup.select('h1,h2,h3,h4,h5,h6') if h.get('id')}
    fragment = BeautifulSoup(markdown(content), 'html.parser')
    used = set()
    for heading in fragment.select('h1,h2,h3,h4,h5,h6'):
        text = ' '.join(heading.get_text().split())
        slug = old.get(text) or re.sub(r'\s+', '-', re.sub(r'[^a-zA-Z0-9\s-]', '', text).strip().lower()) or 'section'
        key, number = slug, 2
        while key in used:
            key = f'{slug}-{number}'
            number += 1
        heading['id'] = key
        used.add(key)
    for anchor in fragment.find_all('a', href=True):
        href = anchor['href']
        target = href.split('#', 1)[0]
        if not re.match(r'^[a-z]+:|^[/#]', href) and target.endswith(('.md','.json')):
            if not (path.parent / target).is_file():
                anchor['href'] = source_base + href
    article = soup.find('article')
    if article is None:
        raise RuntimeError(f'Missing existing article: {path}')
    article.clear()
    for node in list(fragment.contents):
        article.append(node.extract())
    path.write_text(str(soup), encoding='utf-8', newline='\n')


def install_notice(path, consumer, tag):
    soup = BeautifulSoup(path.read_text(encoding='utf-8-sig'), 'html.parser')
    old = soup.find(id='infrastructure-install')
    if old:
        old.decompose()
    notice = BeautifulSoup(
        '<section id="infrastructure-install" style="max-width:1200px;margin:20px auto;padding:12px 20px;border-left:3px solid #548574">'
        f'<strong>Install first:</strong> <a href="{BASE}">TomRenderInfrastructure KKS 0.1.0</a>. '
        'Runtime DLLs and Probe/Feather resources are supplied by the shared base, not this shader archive. '
        f'<a href="https://github.com/tomTom1010-IEE/{consumer}/releases/tag/{tag}">Download the Infra1 shader pack</a>.'
        '</section>', 'html.parser').section
    main = soup.find('main')
    if main is None:
        raise RuntimeError(f'Missing main element: {path}')
    main.insert(0, notice)
    old_tag = 'v0.4.0-20261024' if consumer == 'TomshadersX' else 'v0.2.0-rc1'
    for anchor in soup.find_all('a', href=True):
        if anchor.find_parent('article'):
            continue
        if anchor['href'] == f'https://github.com/tomTom1010-IEE/{consumer}/releases/tag/{old_tag}':
            anchor['href'] = f'https://github.com/tomTom1010-IEE/{consumer}/releases/tag/{tag}'
            anchor.string = 'Shader download'
    path.write_text(str(soup), encoding='utf-8', newline='\n')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tomx', type=Path, required=True)
    parser.add_argument('--bsdf', type=Path, required=True)
    args = parser.parse_args()
    x = args.tomx
    for language, filename in [('en','UserManual.html'),('zh-CN','UserManual.zh-CN.html')]:
        source = x / f'Documents/TomShadersX-UserManual.{language}.md'
        content = source.read_text(encoding='utf-8-sig')
        (x / f'docs/TomShadersX-UserManual.{language}.md').write_text(content, encoding='utf-8', newline='\n')
        refresh_manual(x / 'docs' / filename, content,
                       'https://github.com/tomTom1010-IEE/TomshadersX/blob/main/Documents/')
    b = args.bsdf
    docs = b / 'Assets/Mods/TomShadersBSDF/Documents'
    content = '# TomBSDF User Manual\n\nFor **0.2.0 RC1 / Infra1**, KKS / Built-in Forward / D3D11. '
    content += f'Install [TomRenderInfrastructure 0.1.0]({BASE}) first; shader releases no longer bundle helper DLLs.\n\n'
    for chapter in CHAPTERS:
        content += re.sub(r'^(#{1,5}) ', r'\1# ', (docs/chapter).read_text(encoding='utf-8-sig'), flags=re.M) + '\n\n'
    source_base = 'https://github.com/tomTom1010-IEE/TomBSDF/blob/main/Assets/Mods/TomShadersBSDF/Documents/'
    exported = re.sub(r'\]\(([^#:()]+\.(?:md|json))(#[^)]*)?\)',
                      lambda m: '](' + source_base + m[1] + (m[2] or '') + ')', content)
    (b/'docs/TomBSDF-UserManual.en.md').write_text(exported.rstrip() + '\n', encoding='utf-8', newline='\n')
    refresh_manual(b/'docs/UserManual.html', content, source_base)
    count = 0
    for root,consumer,tag in [(x,'TomshadersX','v0.4.0-infra.1'),(b,'TomBSDF','v0.2.0-rc1-infra.1')]:
        for path in (root/'docs').glob('*.html'):
            install_notice(path,consumer,tag)
            count += 1
    print(f'Refreshed three manuals and installation notices in {count} HTML files; no image files changed.')


if __name__ == '__main__':
    main()
