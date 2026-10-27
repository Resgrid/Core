"""Writes IncidentBackOffice.{locale}.resx from a translated key map.

Usage: python write_locale_resx.py <locale> <translations.json>

The JSON is a flat {key: text} map for the keys of out/en.json (produced by gen_resx.py). A key missing from the map, or a
translation that drops one of the English {n} placeholders, is reported and the run fails, so a partial translation never
ships silently.
"""
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from gen_resx import OUT_DIR, OUT_JSON, write  # noqa: E402


def main(locale, source):
    english = json.load(io.open(os.path.join(OUT_JSON, 'en.json'), encoding='utf-8'))
    translated = json.load(io.open(source, encoding='utf-8'))
    problems = []
    data = {}
    for key, text in english.items():
        value = translated.get(key)
        if not value:
            problems.append('missing ' + key)
            continue
        if sorted(re.findall(r'\{\d+\}', text)) != sorted(re.findall(r'\{\d+\}', value)):
            problems.append('placeholders ' + key)
            continue
        data[key] = value
    if problems:
        print('\n'.join(problems[:50]))
        raise SystemExit('%d problem(s) in %s' % (len(problems), source))
    write(os.path.join(OUT_DIR, 'IncidentBackOffice.%s.resx' % locale), data)
    print(locale, len(data))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
