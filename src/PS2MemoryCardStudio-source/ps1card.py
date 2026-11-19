"""PS1 raw memory cards and MCS single-save imports.

Format reference: https://psx-spx.consoledev.net/dev/ps1/sio/controllersandmemorycards/memory-card-data-format/
"""
from pathlib import Path
import os, shutil, struct, tempfile, time

FRAME=128
BLOCK=8192
CARD=16*BLOCK

def checksum(frame):
    value=0
    for byte in frame[:127]: value ^= byte
    return value

def read_sources(path):
    data=path.read_bytes()
    if path.suffix.lower()=='.mcs':
        if len(data)<FRAME+BLOCK or (len(data)-FRAME)%BLOCK or len(data)>FRAME+15*BLOCK:
            raise ValueError('Invalid MCS length.')
        frame=data[:FRAME]; payload=data[FRAME:]
        if frame[0]!=0x51 or checksum(frame)!=frame[127]:
            raise ValueError('Invalid MCS directory frame or checksum.')
        size=struct.unpack_from('<I',frame,4)[0]
        if not size or (size+BLOCK-1)//BLOCK!=len(payload)//BLOCK:
            raise ValueError('MCS block count does not match its directory entry.')
        return [(frame,payload)]
    if len(data)!=CARD or data[:2]!=b'MC':
        raise ValueError('Expected a raw 128 KiB PS1 memory card or an MCS save.')
    saves=[]; seen=set()
    for block in range(1,16):
        frame=data[block*FRAME:(block+1)*FRAME]
        if frame[0]!=0x51: continue
        size=struct.unpack_from('<I',frame,4)[0]
        count=(size+BLOCK-1)//BLOCK
        if not 1<=count<=15: raise ValueError('Invalid PS1 save block count.')
        chunks=[]; cur=block
        for i in range(count):
            if not 1<=cur<=15 or cur in seen: raise ValueError('Invalid or overlapping PS1 block chain.')
            seen.add(cur); entry=data[cur*FRAME:(cur+1)*FRAME]
            expected=0x51 if i==0 else (0x53 if i==count-1 else 0x52)
            if entry[0]!=expected or checksum(entry)!=entry[127]: raise ValueError('Invalid PS1 directory frame or checksum.')
            chunks.append(data[cur*BLOCK:(cur+1)*BLOCK]); nxt=struct.unpack_from('<H',entry,8)[0]
            if i==count-1 and nxt!=0xffff: raise ValueError('PS1 block chain is longer than declared.')
            cur=nxt+1
        saves.append((frame,b''.join(chunks)))
    return saves

def create_ps1(save_paths, template, output):
    template=Path(template); output=Path(output)
    card=bytearray(template.read_bytes())
    if len(card)!=CARD or card[:2]!=b'MC' or any(card[i*FRAME] != 0xa0 or checksum(card[i*FRAME:(i+1)*FRAME])!=card[(i+1)*FRAME-1] for i in range(1,16)):
        raise ValueError('The bundled blank PS1 card is invalid.')
    imported=[]; names=set(); next_block=1
    for path in save_paths:
        entries=read_sources(path)
        if not entries: raise ValueError(f'{path.name}: card contains no saves.')
        for frame,payload in entries:
            name=frame[10:31].split(b'\0',1)[0]
            if not name: raise ValueError(f'{path.name}: save has no directory name.')
            if name in names: raise ValueError(f'{path.name}: duplicate save directory {name.decode("ascii", "replace")}.')
            names.add(name); count=len(payload)//BLOCK
            if next_block+count>16: raise ValueError('These PS1 saves need more than the 15 available blocks. Remove saves and try again.')
            for n in range(count):
                slot=next_block+n
                entry=bytearray(frame if n==0 else bytes(FRAME))
                entry[0]=0x51 if n==0 else (0x53 if n==count-1 else 0x52)
                struct.pack_into('<H',entry,8,0xffff if n==count-1 else slot)
                entry[127]=checksum(entry)
                card[slot*FRAME:(slot+1)*FRAME]=entry
                card[slot*BLOCK:(slot+1)*BLOCK]=payload[n*BLOCK:(n+1)*BLOCK]
            next_block+=count
            imported.append({'file':path.name,'directory':name.decode('ascii','replace'),'format':path.suffix})
    fd,temp=tempfile.mkstemp(prefix='.ps1card-',suffix='.tmp',dir=output.parent)
    try:
        with os.fdopen(fd,'wb') as f: f.write(card)
        backup=None
        if output.exists():
            backup=output.with_name(f'{output.stem}.{time.strftime("%Y%m%d-%H%M%S")}.bak{output.suffix}')
            shutil.copy2(output,backup)
        os.replace(temp,output)
    finally:
        Path(temp).unlink(missing_ok=True)
    return {'imported':imported,'failures':[],'template':template,'backup':backup,'free':(16-next_block)*BLOCK,'total':15*BLOCK}
