var j = require('./testData');
console.log(j.Composite.C1);

var fs = require('fs');
fs.readFile('key.pem', (err, data) => {
	if (err)
		console.log(err);
	else {
		var j = { Data: data.toString() };
		console.log(j);
		fs.writeFile('key.json', JSON.stringify(j, null, 4), (err) => {
			if (err)
				console.log(err);
		});
	}
});

fs.readFile('key.json', (err, data) => {
	if (err)
		console.log(err);
	else {
		console.log(JSON.parse(data).Data);
	}
});
