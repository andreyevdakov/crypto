var crypto = require('crypto');
var fs = require('fs');
/*
var privatePem = fs.readFileSync('key.pem');
var publicPem = fs.readFileSync('cert.pem');
var key = privatePem.toString();
var pubkey = publicPem.toString();

var data = "abcdef"

var sign = crypto.createSign('RSA-SHA512');
sign.update(data);
var sig = sign.sign(key, 'base64');
console.log('signature: ', sig);

var verify = crypto.createVerify('RSA-SHA512');
verify.update(data);
var result = verify.verify(pubkey, sig, 'base64');
console.log('verification: ', result);
*/

var pem = fs.readFileSync('slsrvmgr.ske');
var key = pem.toString('ascii');
var hmac = crypto.createHmac('sha512', key);
hmac.update('foosaljf;asdj;asjd;alsjd;flsjdfsidpf89we8fpw98efpw89euf');
console.log(hmac.digest('base64'));
