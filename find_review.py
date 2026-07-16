import sqlite3

db_path = r'C:\Users\gjr\.local\share\mimocode\mimocode.db'
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Search for Chinese characters '检查' (check)
cursor.execute("""
    SELECT count(*) FROM part p
    JOIN message m ON p.message_id = m.id
    WHERE json_extract(m.data, '$.role') = 'user'
      AND json_extract(p.data, '$.type') = 'text'
      AND json_extract(p.data, '$.text') LIKE '%检查%'
""")
cnt = cursor.fetchone()[0]
print(f"Chinese '检查' found {cnt} times")

# Search for English 'review'
cursor.execute("""
    SELECT count(*) FROM part p
    JOIN message m ON p.message_id = m.id
    WHERE json_extract(m.data, '$.role') = 'user'
      AND json_extract(p.data, '$.type') = 'text'
      AND json_extract(p.data, '$.text') LIKE '%review%'
""")
cnt2 = cursor.fetchone()[0]
print(f"English 'review' found {cnt2} times")

# Search for 'audit'
cursor.execute("""
    SELECT count(*) FROM part p
    JOIN message m ON p.message_id = m.id
    WHERE json_extract(m.data, '$.role') = 'user'
      AND json_extract(p.data, '$.type') = 'text'
      AND json_extract(p.data, '$.text') LIKE '%audit%'
""")
cnt3 = cursor.fetchone()[0]
print(f"English 'audit' found {cnt3} times")

# Show sample of 'review' messages
cursor.execute("""
    SELECT json_extract(p.data, '$.text') FROM part p
    JOIN message m ON p.message_id = m.id
    WHERE json_extract(m.data, '$.role') = 'user'
      AND json_extract(p.data, '$.type') = 'text'
      AND json_extract(p.data, '$.text') LIKE '%review%'
    LIMIT 5
""")
samples = cursor.fetchall()
print("\nSample 'review' messages:")
for s in samples:
    print(s[0][:200])
    print("---")

conn.close()