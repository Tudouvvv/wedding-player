#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
婚礼影音播放器 - 本地服务（备用 Python 版）
提示：优先使用同目录的 wedding-server.exe（无需任何依赖）。
本脚本需要 Python 3 与 pycryptodome，安装：pip install pycryptodome
提供歌单导入所需的 API 代理（网易云 EAPI / QQ音乐 / 华为音乐），并托管播放器页面。
仅监听 127.0.0.1，不暴露到局域网。
"""
import os
import sys
import json
import time
import random
import hashlib
import webbrowser
import http.server
import socketserver
import urllib.request
import urllib.parse

PORT_START = int(os.environ.get('WEDDING_PORT', '8099'))
ROOT = os.path.dirname(os.path.abspath(__file__))

try:
    from Crypto.Cipher import AES
    HAVE_AES = True
except ImportError:
    HAVE_AES = False

UA_ANDROID = ('Mozilla/5.0 (Linux; Android 11; M2007J3SC Build/RKQ1.200826.002; wv) '
              'AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/77.0.3865.120 '
              'MQQBrowser/6.2 TBS/045714 Mobile Safari/537.36 NeteaseMusic/8.7.01')


def eapi_encrypt(url_path, text):
    msg = 'nobody' + url_path + 'use' + text + 'md5forencrypt'
    digest = hashlib.md5(msg.encode('utf-8')).hexdigest()
    data = (url_path + '-36cd479b6b5-' + text + '-36cd479b6b5-' + digest).encode('utf-8')
    pad_len = 16 - len(data) % 16
    data += bytes([pad_len]) * pad_len
    cipher = AES.new(b'e82ckenh8dichen8', AES.MODE_ECB)
    return cipher.encrypt(data).hex().upper()


def fetch_post(url, form_body, referer, cookie, timeout=20):
    req = urllib.request.Request(url, data=form_body.encode('utf-8'), method='POST')
    req.add_header('User-Agent', UA_ANDROID)
    req.add_header('Content-Type', 'application/x-www-form-urlencoded')
    req.add_header('Accept', '*/*')
    if referer:
        req.add_header('Referer', referer)
    if cookie:
        req.add_header('Cookie', cookie)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


QR_KEY_COOKIES = {}


def qr_unikey():
    device_id = os.urandom(16).hex().upper()
    ts = str(int(time.time() * 1000))
    request_id = ts + '_' + str(random.randint(0, 999)).zfill(4)
    cookie = ('osver=android; appver=8.7.01; os=android; deviceId=' + device_id +
              '; channel=netease; requestId=' + request_id + '; __remember_me=true')
    form = 'params=' + eapi_encrypt('/api/login/qrcode/unikey', '{"type":1}')
    req = urllib.request.Request('http://music.163.com/eapi/login/qrcode/unikey', data=form.encode('utf-8'), method='POST')
    req.add_header('User-Agent', UA_ANDROID)
    req.add_header('Content-Type', 'application/x-www-form-urlencoded')
    req.add_header('Referer', 'music.163.com')
    req.add_header('Cookie', cookie)
    with urllib.request.urlopen(req, timeout=20) as resp:
        body = resp.read().decode('utf-8', 'ignore')
        set_cookie = resp.headers.get('Set-Cookie', '') or ''
    import re as _re
    m = _re.search(r'"unikey":"([^"]+)"', body)
    unikey = m.group(1) if m else ''
    nmtid = ''
    m2 = _re.search(r'NMTID=([^;,\s]+)', set_cookie)
    if m2:
        nmtid = 'NMTID=' + m2.group(1)
    if unikey:
        QR_KEY_COOKIES[unikey] = nmtid
    return json.dumps({'code': 200, 'unikey': unikey, 'qrUrl': 'https://music.163.com/login?codekey=' + unikey}).encode('utf-8')


def tx_sign(payload):
    import hashlib, base64
    h = hashlib.sha1(payload.encode('utf-8')).hexdigest().upper()
    part1_idx = [23, 14, 6, 36, 16, 7, 19]
    part2_idx = [16, 1, 32, 12, 19, 27, 8, 5]
    scramble = [89, 39, 179, 150, 218, 82, 58, 252, 177, 52, 186, 123, 120, 64, 242, 133, 143, 161, 121, 179]
    part1 = ''.join(h[i] for i in part1_idx)
    part2 = ''.join(h[i] for i in part2_idx)
    part3 = bytes([scramble[i] ^ int(h[i * 2:i * 2 + 2], 16) for i in range(20)])
    b64 = base64.b64encode(part3).decode().replace('/', '').replace('+', '').replace('=', '')
    return ('zzc' + part1 + b64 + part2).lower()


def qq_lyric(songmid):
    import re as _re, base64
    def _comm():
        return ('{"v":14090008,"ct":11,"cv":14090008,"chid":"2005000982","QIMEI":"","QIMEI36":"","tmeAppID":"qqmusic","format":"json","inCharset":"utf-8","outCharset":"utf-8",'
                '"OpenUDID":"ffffffffbff94f7d000000000033c587","udid":"ffffffffbff94f7d000000000033c587","os_ver":"10","aid":"d2550265db4ce5c4","phonetype":"MI 6","devicelevel":29,'
                '"newdevicelevel":29,"nettype":"1030","rom":"xiaomi/iarim/sagit:10/eomam.200122.001/1:user/release-keys","OpenUDID2":"ffffffffbff94f7d000001999ff7d5bf","qq":"0"}')
    def _post(req_json):
        sign = tx_sign(req_json)
        req = urllib.request.Request('https://u.y.qq.com/cgi-bin/musics.fcg?sign=' + sign, data=req_json.encode('utf-8'), method='POST')
        req.add_header('User-Agent', UA_ANDROID)
        req.add_header('Content-Type', 'application/json')
        with urllib.request.urlopen(req, timeout=20) as resp:
            return resp.read().decode('utf-8', 'ignore')
    # songmid -> id
    detail = '{"comm":' + _comm() + ',"req":{"module":"music.pf_song_detail_svr","method":"get_song_detail_yqq","param":{"song_type":0,"song_mid":"' + songmid + '"}}}'
    dtext = _post(detail)
    m = _re.search(r'"track_info":{"id":(d+)', dtext)
    if not m:
        return json.dumps({'lyric': ''}).encode('utf-8')
    sid = m.group(1)
    lyric = '{"comm":' + _comm() + ',"req":{"method":"GetPlayLyricInfo","module":"music.musichallSong.PlayLyricInfo","param":{"format":"json","crypt":0,"ct":19,"cv":1873,"interval":0,"lrc_t":0,"qrc":0,"qrc_t":0,"roma":0,"roma_t":0,"songID":' + sid + ',"trans":0,"trans_t":0,"type":-1}}}'
    ltext = _post(lyric)
    m2 = _re.search(r'"lyric":"([^"]*)"', ltext)
    if m2 and m2.group(1):
        try:
            lrc = base64.b64decode(m2.group(1)).decode('utf-8', 'ignore')
            return json.dumps({'lyric': lrc}).encode('utf-8')
        except Exception:
            pass
    return json.dumps({'lyric': ''}).encode('utf-8')


def qq_playurl(songmid, media_mid, qcookie):
    import re as _re
    if not media_mid:
        media_mid = songmid
    uin = '0'
    m = _re.search(r'uin=o?(\d+)', qcookie)
    if m:
        uin = m.group(1)
    token = ''
    m2 = _re.search(r'qqmusic_key=([^;]+)', qcookie)
    if not m2:
        m2 = _re.search(r'qm_keyst=([^;]+)', qcookie)
    if m2:
        token = m2.group(1)
    auth = ',\"qq\":\"' + uin + '\"' if uin != '0' and token else ',\"qq\":\"0\"'
    if uin != '0' and token:
        auth += ',\"authst\":\"' + token + '\",\"tmeLoginType\":' + ('1' if token.startswith('W_X_') else '2')
    comm = ('{\"v\":14090008,\"ct\":11,\"cv\":14090008,\"chid\":\"2005000982\",\"QIMEI\":\"\",\"QIMEI36\":\"\",'
            '\"tmeAppID\":\"qqmusic\",\"format\":\"json\",\"inCharset\":\"utf-8\",\"outCharset\":\"utf-8\",'
            '\"OpenUDID\":\"ffffffffbff94f7d000000000033c587\",\"udid\":\"ffffffffbff94f7d000000000033c587\",'
            '\"os_ver\":\"10\",\"aid\":\"d2550265db4ce5c4\",\"phonetype\":\"MI 6\",\"devicelevel\":29,'
            '\"newdevicelevel\":29,\"nettype\":\"1030\",\"rom\":\"xiaomi/iarim/sagit:10/eomam.200122.001/1:user/release-keys\",'
            '\"OpenUDID2\":\"ffffffffbff94f7d000001999ff7d5bf\"' + auth + '}')
    body = ('{\"comm\":' + comm + ',\"request\":{\"module\":\"music.vkey.GetVkey\",\"method\":\"UrlGetVkey\",'
            '\"param\":{\"guid\":\"Feixiao\",\"uin\":\"' + uin + '\",\"downloadfrom\":1,\"ctx\":1,'
            '\"referer\":\"y.qq.com\",\"scene\":0,\"songtype\":[1],\"songmid\":[\"' + songmid + '\"],'
            '\"filename\":[\"M500' + media_mid + '.mp3\"]}}}')
    body = body.replace('\\"', '\"')
    sign = tx_sign(body)
    req = urllib.request.Request('https://u.y.qq.com/cgi-bin/musics.fcg?sign=' + sign, data=body.encode('utf-8'), method='POST')
    req.add_header('User-Agent', UA_ANDROID)
    req.add_header('Content-Type', 'application/json')
    with urllib.request.urlopen(req, timeout=20) as resp:
        text = resp.read().decode('utf-8', 'ignore')
    m3 = _re.search(r'"purl":"([^"]*)"', text)
    if m3 and m3.group(1):
        return json.dumps({'url': 'https://isure.stream.qqmusic.qq.com/' + m3.group(1)}).encode('utf-8')
    return json.dumps({'url': ''}).encode('utf-8')


def qq_resolve(share_url):
    try:
        req = urllib.request.Request(share_url)
        req.add_header('User-Agent', UA_ANDROID)
        with urllib.request.urlopen(req, timeout=15) as resp:
            final = resp.geturl()
        return json.dumps({'url': final}).encode('utf-8')
    except Exception as e:
        return json.dumps({'url': '', 'error': str(e)}).encode('utf-8')


def qr_check(key):
    ck = QR_KEY_COOKIES.get(key, '')
    device_id = os.urandom(16).hex().upper()
    ts = str(int(time.time() * 1000))
    request_id = ts + '_' + str(random.randint(0, 999)).zfill(4)
    cookie = ('osver=android; appver=8.7.01; os=android; deviceId=' + device_id +
              '; channel=netease; requestId=' + request_id + '; __remember_me=true')
    if ck:
        cookie += '; ' + ck
    form = 'params=' + eapi_encrypt('/api/login/qrcode/client/login', '{"type":1,"key":"' + key + '"}')
    req = urllib.request.Request('http://music.163.com/eapi/login/qrcode/client/login', data=form.encode('utf-8'), method='POST')
    req.add_header('User-Agent', UA_ANDROID)
    req.add_header('Content-Type', 'application/x-www-form-urlencoded')
    req.add_header('Referer', 'music.163.com')
    req.add_header('Cookie', cookie)
    with urllib.request.urlopen(req, timeout=20) as resp:
        body = resp.read().decode('utf-8', 'ignore')
        set_cookie = resp.headers.get('Set-Cookie', '') or ''
    import re as _re
    code = '0'
    m = _re.search(r'"code":(\d+)', body)
    if m:
        code = m.group(1)
    msg = ''
    m2 = _re.search(r'"message":"([^"]*)"', body)
    if m2:
        msg = m2.group(1)
    music_u = ''
    m3 = _re.search(r'MUSIC_U=([^;,\s]+)', set_cookie)
    if m3:
        music_u = 'MUSIC_U=' + m3.group(1)
    if code == '803':
        QR_KEY_COOKIES.pop(key, None)
    return json.dumps({'code': int(code), 'message': msg, 'musicU': music_u}).encode('utf-8')


def fetch_get(url, referer=None, cookie=None, timeout=20):
    req = urllib.request.Request(url)
    req.add_header('User-Agent', UA_ANDROID)
    if referer:
        req.add_header('Referer', referer)
    if cookie:
        req.add_header('Cookie', cookie)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def eapi_call(api_path, text, extra_cookie=''):
    device_id = os.urandom(16).hex().upper()
    ts = str(int(time.time() * 1000))
    request_id = ts + '_' + str(random.randint(0, 999)).zfill(4)
    cookie = ('osver=android; appver=8.7.01; os=android; deviceId=' + device_id +
              '; channel=netease; requestId=' + request_id + '; __remember_me=true')
    if extra_cookie:
        cookie += '; ' + extra_cookie
    form = 'params=' + eapi_encrypt(api_path, text)
    url = 'http://music.163.com/eapi/' + api_path[len('/api/'):]
    return fetch_post(url, form, 'music.163.com', cookie)


class Handler(http.server.SimpleHTTPRequestHandler):

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path.startswith('/api/'):
            self.handle_api(parsed)
            return
        if parsed.path in ('/', ''):
            self.path = '/wedding-player.html'
        super().do_GET()

    def handle_api(self, parsed):
        qs = urllib.parse.parse_qs(parsed.query)

        def g(key):
            return (qs.get(key) or [''])[0]

        try:
            path = parsed.path
            if path == '/api/config':
                cf = os.path.join(ROOT, 'config.json')
                if os.path.exists(cf):
                    with open(cf, 'rb') as f:
                        body = f.read()
                else:
                    body = b'{}'
                self.send_response(200)
                self.send_header('Content-Type', 'application/json; charset=utf-8')
                self.send_header('Content-Length', str(len(body)))
                self.end_headers()
                self.wfile.write(body)
                return
            ucookie = g('cookie')
            if path == '/api/netease/playlist':
                body = eapi_call('/api/v6/playlist/detail', '{"s":"0","id":"' + g('id') + '","n":"1000","t":"0"}', ucookie)
            elif path == '/api/netease/search':
                s = json.dumps(g('s'), ensure_ascii=False)
                body = eapi_call('/api/cloudsearch/pc', '{"s":' + s + ',"type":1,"limit":8,"total":"true","offset":0}', ucookie)
            elif path == '/api/netease/url':
                body = eapi_call('/api/song/enhance/player/url', '{"ids":[' + g('id') + '],"br":128000}', ucookie)
            elif path == '/api/netease/lyric':
                body = eapi_call('/api/song/lyric', '{"id":"' + g('id') + '","os":"linux","lv":-1,"kv":-1,"tv":-1}', ucookie)
            elif path == '/api/netease/qrkey':
                body = qr_unikey()
            elif path == '/api/netease/qrcheck':
                body = qr_check(g('key'))
            elif path == '/api/qq/playurl':
                body = qq_playurl(g('songmid'), g('mediaMid'), g('cookie'))
            elif path == '/api/qq/lyric':
                body = qq_lyric(g('songmid'))
            elif path == '/api/qq/resolve':
                body = qq_resolve(g('url'))
            elif path == '/api/qq/playlist':
                url = ('https://c.y.qq.com/qzone/fcg-bin/fcg_ucc_getcdinfo_byids_cp.fcg'
                       '?type=1&json=1&utf8=1&onlysong=0&disstid=' + g('id') +
                       '&format=json&g_tk=1913204067&loginUin=0&hostUin=0&inCharset=utf8'
                       '&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=0')
                body = fetch_get(url, referer='https://y.qq.com/')
            elif path == '/api/huawei/playlist':
                body = fetch_get(g('url'))
            else:
                self.send_error(404)
                return
            self.send_response(200)
            self.send_header('Content-Type', 'application/json; charset=utf-8')
            self.send_header('Cache-Control', 'no-store')
            self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except Exception as exc:
            err = json.dumps({'error': str(exc)}).encode('utf-8')
            self.send_response(502)
            self.send_header('Content-Type', 'application/json; charset=utf-8')
            self.send_header('Content-Length', str(len(err)))
            self.end_headers()
            self.wfile.write(err)

    def do_POST(self):
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path == '/api/config':
            try:
                length = int(self.headers.get('Content-Length') or 0)
                body = self.rfile.read(length) if length > 0 else b'{}'
                with open(os.path.join(ROOT, 'config.json'), 'wb') as f:
                    f.write(body)
                self.send_response(200)
                self.send_header('Content-Type', 'application/json; charset=utf-8')
                self.end_headers()
                self.wfile.write(b'{"ok":true}')
            except Exception as e:
                self.send_response(500)
                self.send_header('Content-Type', 'application/json; charset=utf-8')
                self.end_headers()
                self.wfile.write(json.dumps({'ok': False, 'error': str(e)}).encode('utf-8'))
        else:
            self.send_response(404)
            self.end_headers()

    def log_message(self, fmt, *args):
        sys.stderr.write('[server] %s\n' % (fmt % args))


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main():
    if not HAVE_AES:
        print('缺少加密库 pycryptodome。')
        print('请先运行: pip install pycryptodome')
        print('或直接使用同目录的 wedding-server.exe（推荐，无需任何依赖）。')
        input('按回车键退出...')
        return
    srv = None
    port = PORT_START
    for p in range(port, port + 10):
        try:
            srv = Server(('127.0.0.1', p), Handler)
            port = p
            break
        except OSError:
            continue
    if srv is None:
        print('启动失败：端口被占用')
        input('按回车键退出...')
        return
    url = 'http://127.0.0.1:%d/wedding-player.html' % port
    print('')
    print('===================================================')
    print('  婚礼影音播放器 本地服务已启动')
    print('  ' + url)
    print('  浏览器将自动打开；关闭本窗口即停止服务')
    print('===================================================')
    print('')
    try:
        webbrowser.open(url)
    except Exception:
        pass
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == '__main__':
    main()
