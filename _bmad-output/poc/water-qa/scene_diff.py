import re,sys,collections
def docs(p):
    t=open(p,encoding='utf-8',newline='').read().replace('\r\n','\n')
    parts=re.split(r'^--- !u!(\d+) &(-?\d+)[^\n]*\n',t,flags=re.M)
    d={}
    for i in range(1,len(parts),3): d[parts[i+1]]=(parts[i],parts[i+2])
    return d
a=docs(sys.argv[1]); b=docs(sys.argv[2])
added=[k for k in b if k not in a]; removed=[k for k in a if k not in b]
changed=[k for k in a if k in b and a[k]!=b[k]]
print('docs head',len(a),'now',len(b),'added',len(added),'removed',len(removed),'changed',len(changed))
c=collections.Counter()
for k in changed:
    la=a[k][1].splitlines(); lb=b[k][1].splitlines()
    diffs=[(x,y) for x,y in zip(la,lb) if x!=y]
    key=tuple(sorted(set(re.sub(r'[-\d.]+','N',x.strip()) for x,_ in diffs)))+(('LEN' if len(la)!=len(lb) else ''),)
    c[key]+=1
for k,v in c.most_common(): print(v,k)
ca=collections.Counter(b[k][0] for k in added); print('added classes',dict(ca))
names=[re.search(r'm_Name: (.*)',b[k][1]).group(1) for k in added if b[k][0]=='1']
print('added GO names',collections.Counter(re.sub(r'\d+','N',n) for n in names))
