import sys

path = sys.argv[1]
raw = open(path, encoding='utf-8').read()
BS = chr(92)
QUOTE = chr(34)
i, n, tokens = 0, len(raw), []
while i < n:
    c = raw[i]
    if c in ' \t\r\n':
        i += 1
    elif c in '{}':
        tokens.append(c)
        i += 1
    elif c == QUOTE:
        i += 1
        buf = []
        while i < n and raw[i] != QUOTE:
            if raw[i] == BS and i + 1 < n and raw[i + 1] == QUOTE:
                buf.append(QUOTE)
                i += 2
            else:
                buf.append(raw[i])
                i += 1
        assert i < n, 'unterminated string'
        i += 1
        tokens.append('S:' + ''.join(buf))
    else:
        j = i
        while j < n and raw[j] not in ' \t\r\n{}' + QUOTE:
            j += 1
        tokens.append('W:' + raw[i:j])
        i = j

depth, keys, ok, k = 0, 0, True, 0
while k < len(tokens):
    t = tokens[k]
    if t == '{':
        depth += 1
    elif t == '}':
        depth -= 1
        if depth < 0:
            ok = False
            print('extra close brace')
            break
    else:
        if k + 1 >= len(tokens):
            ok = False
            print('dangling key:', t[:40])
            break
        nxt = tokens[k + 1]
        if nxt == '{':
            k += 1
            depth += 1  # the open brace is consumed by the k+=1 below; count it here
        elif nxt.startswith('S:') or nxt.startswith('W:'):
            keys += 1
            k += 1
        else:
            ok = False
            print('value missing after', t[:40])
            break
    k += 1

print('balanced+paired:', ok, '| depth end:', depth, '| kv pairs:', keys)
changenote = [t[2:] for t in tokens if t.startswith('S:') and t[2:].startswith('v0.1.6')]
print('changenote starts v0.1.6:', bool(changenote), '| length:', len(changenote[0]) if changenote else 0)
print('no internal double quotes in changenote:', QUOTE not in (changenote[0] if changenote else ''))
descs = [t[2:] for t in tokens if t.startswith('S:') and t[2:].startswith('[h1]')]
print('description found:', bool(descs), '| length:', len(descs[0]) if descs else 0)
print('[/list] balance in description:', (descs[0].count('[list]'), descs[0].count('[/list]')) if descs else '-')
