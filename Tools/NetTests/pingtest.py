import socket, sys, time, json
port = int(sys.argv[1]); split = sys.argv[2] == "split"
msg = bytes([1]) + b"" + bytes(6144 - 1)          # ping, empty password, padded to 6144 like the launcher
s = socket.create_connection(("127.0.0.1", port), timeout=5)
s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
if split:
    for a, b in ((0, 1500), (1500, 4000), (4000, 6144)):
        s.sendall(msg[a:b]); time.sleep(0.25)
else:
    s.sendall(msg)
try:
    data = s.recv(65536)
    print("response:", data[:160].decode("utf-8", "replace") if data else "(connection closed, no data)")
except Exception as e:
    print("no response:", type(e).__name__, e)
