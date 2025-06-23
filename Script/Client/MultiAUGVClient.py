import socket
import json

routes = {
    "AUGV_1": ["Warehouse_1", "Warehouse_12", "Warehouse_7", "AUGV_1_Loadingspot"],
    "AUGV_2": ["Warehouse_5", "Warehouse_12", "Warehouse_8", "AUGV_2_Loadingspot"],
    "AUGV_3": ["Warehouse_3", "Warehouse_10", "AUGV_3_Loadingspot"],
    "AUGV_4": ["Warehouse_2", "Warehouse_11", "Warehouse_6", "AUGV_4_Loadingspot"],
    "AUGV_5": ["Warehouse_4", "AUGV_5_Loadingspot"]
}

s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
s.connect(("localhost", 8051))
s.send(json.dumps(routes).encode())
s.close()
