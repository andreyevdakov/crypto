var crypto = require('crypto');
var fs = require('fs');

var pem = fs.readFileSync('slsrvmgr.ske');
var key = pem.toString('ascii');
var hmac = crypto.createHmac('sha512', key);
hmac.update('foosaljf;asdj;asjd;alsjd;flsjdfsidpf89we8fpw98efpw89euf');
console.log(hmac.digest('base64'));
