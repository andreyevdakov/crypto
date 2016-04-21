var crypto = require('crypto');
var fs = require('fs');

var privatePem = fs.readFileSync('key.pem');
var publicPem = fs.readFileSync('cert.pem');
var key = privatePem.toString();
var pubkey = publicPem.toString();

var data = fs.readFileSync('SLBoost.ini');

var sign = crypto.createSign('RSA-SHA512');
sign.update(data);
var sig = sign.sign(key);

var pos = sig.length;
var buffer = new Buffer(pos);
for (var b of sig) {
	pos--;
	buffer[pos] = b;
}
var sigStr = buffer.toString('base64');
console.log('Signature: ' + sigStr);
fs.writeFileSync('signature.txt', sigStr);

var verify = crypto.createVerify('RSA-SHA512');
verify.update(data);
var result = verify.verify(pubkey, sig, 'base64');
console.log('verification: ', result);
