import socket, sys, time, struct
port, split, name = int(sys.argv[1]), sys.argv[2] == "split", sys.argv[3]
nb = name.encode()
model = b"Jugador1ModelNameLongForASpecificReason"
body = bytes([2, len(nb)]) + nb + bytes([0]) + bytes([1]) + b"0" + struct.pack(">h", len(model)) + model
msg = body + bytes(7168 - len(body))
s = socket.create_connection(("127.0.0.1", port), timeout=5)
s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
if split:
    s.sendall(msg[:2]); time.sleep(0.4); s.sendall(msg[2:3000]); time.sleep(0.4); s.sendall(msg[3000:])
else:
    s.sendall(msg)
try:
    print("response:", s.recv(4096)[:120])
except Exception as e:
    print("no response:", type(e).__name__)
time.sleep(0.3); s.close()
